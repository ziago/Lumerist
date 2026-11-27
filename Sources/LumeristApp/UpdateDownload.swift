import CryptoKit
import Foundation
import LumeristCore

/// Downloads one release file into the private folder, hashing it as it
/// arrives. Only after the user pressed "Update now".
///
/// - Every step, the first URL and each redirect, must pass
///   `UpdateURLRule.isTrustedHop` (https to GitHub's hosts); at most 5 redirects.
/// - The answer must be 200 and the file exactly the size the API declared
///   (which is under its role's limit): more bytes stop the download at once.
/// - No cookies, cache or stored credentials; timeouts on silence (30 s) and
///   on the whole file (30 min).
/// - The file is created new, readable only by this user (0600).
/// - With the debug test feed, a `file://` asset is read from disk the same way.
final class UpdateDownload: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    struct Result: Sendable {
        let file: URL
        let sha256: String
    }

    enum Failure: Error, LocalizedError {
        case untrustedAddress
        case tooManyRedirects
        case status(Int)
        case wrongSize(expected: Int, received: Int)
        case transport(String)
        case cancelled

        var errorDescription: String? {
            switch self {
            case .untrustedAddress: "The download was sent to an address outside GitHub."
            case .tooManyRedirects: "The download was redirected too many times."
            case .status(let code): "GitHub answered with status \(code)."
            case .wrongSize(let expected, let received): "The file has \(received) bytes instead of \(expected)."
            case .transport(let reason): reason
            case .cancelled: "The download was cancelled."
            }
        }
    }

    private let asset: UpdateAsset
    private let destination: URL
    private let allowFileURLs: Bool
    private let progress: @Sendable (Int) -> Void
    // Touched only on the session's serial delegate queue.
    private var handle: FileHandle?
    private var hasher = SHA256()
    private var received = 0
    private var reported = 0
    private var redirects = 0
    private var failure: Failure?
    private var continuation: CheckedContinuation<Result, Error>?
    private var task: URLSessionDataTask?
    private let queue: OperationQueue = {
        let queue = OperationQueue()
        queue.maxConcurrentOperationCount = 1
        return queue
    }()

    private init(asset: UpdateAsset, folder: URL, allowFileURLs: Bool, progress: @escaping @Sendable (Int) -> Void) {
        self.asset = asset
        destination = folder.appending(path: asset.name)
        self.allowFileURLs = allowFileURLs
        self.progress = progress
    }

    /// Downloads `asset` into `folder` (its own name), or throws.
    static func fetch(_ asset: UpdateAsset, into folder: URL, allowFileURLs: Bool,
                      progress: @escaping @Sendable (Int) -> Void = { _ in }) async throws -> Result {
        let download = UpdateDownload(asset: asset, folder: folder, allowFileURLs: allowFileURLs, progress: progress)
        return try await withTaskCancellationHandler {
            try await download.start()
        } onCancel: {
            download.cancel()
        }
    }

    private func start() async throws -> Result {
        guard UpdateURLRule.isTrustedHop(asset.url, allowFileURLs: allowFileURLs) else { throw Failure.untrustedAddress }
        if asset.url.isFileURL { return try readLocalFile() }
        return try await withCheckedThrowingContinuation { continuation in
            queue.addOperation {
                if let failure = self.failure {
                    continuation.resume(throwing: failure) // cancelled before it started
                    return
                }
                self.continuation = continuation
                let configuration = URLSessionConfiguration.ephemeral
                configuration.httpCookieAcceptPolicy = .never
                configuration.httpShouldSetCookies = false
                configuration.urlCache = nil
                configuration.urlCredentialStorage = nil
                configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
                configuration.timeoutIntervalForRequest = 30
                configuration.timeoutIntervalForResource = 30 * 60
                configuration.waitsForConnectivity = false
                let session = URLSession(configuration: configuration, delegate: self, delegateQueue: self.queue)
                var request = URLRequest(url: self.asset.url)
                request.setValue("application/octet-stream", forHTTPHeaderField: "Accept")
                request.setValue("Lumerist", forHTTPHeaderField: "User-Agent")
                let task = session.dataTask(with: request)
                self.task = task
                task.resume()
                session.finishTasksAndInvalidate()
            }
        }
    }

    private func cancel() {
        queue.addOperation {
            if self.failure == nil { self.failure = .cancelled }
            self.task?.cancel()
        }
    }

    /// The test feed's local files: the same size and hashing rules.
    private func readLocalFile() throws -> Result {
        let source = try FileHandle(forReadingFrom: asset.url)
        defer { try? source.close() }
        let output = try MacUpdate.createPrivateFile(asset.name, in: destination.deletingLastPathComponent())
        defer { try? output.close() }
        var hasher = SHA256(), total = 0
        while let chunk = try source.read(upToCount: 1 << 20), !chunk.isEmpty {
            try Task.checkCancellation()
            total += chunk.count
            guard total <= asset.size else { throw Failure.wrongSize(expected: asset.size, received: total) }
            hasher.update(data: chunk)
            try output.write(contentsOf: chunk)
            progress(total)
        }
        guard total == asset.size else { throw Failure.wrongSize(expected: asset.size, received: total) }
        return Result(file: destination, sha256: UpdateChecksums.hex(hasher.finalize()))
    }

    // MARK: URLSessionDataDelegate (on `queue`)

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        redirects += 1
        guard redirects <= 5 else {
            stop(.tooManyRedirects)
            completionHandler(nil)
            return
        }
        // Never to a local file, whatever the test feed allows.
        guard let url = request.url, UpdateURLRule.isTrustedHop(url, allowFileURLs: false) else {
            stop(.untrustedAddress)
            completionHandler(nil)
            return
        }
        completionHandler(request)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard let http = response as? HTTPURLResponse, let url = http.url,
              UpdateURLRule.isTrustedHop(url, allowFileURLs: false) else {
            stop(.untrustedAddress)
            completionHandler(.cancel)
            return
        }
        guard http.statusCode == 200 else {
            stop(.status(http.statusCode))
            completionHandler(.cancel)
            return
        }
        if http.expectedContentLength > 0, http.expectedContentLength != Int64(asset.size) {
            stop(.wrongSize(expected: asset.size, received: Int(http.expectedContentLength)))
            completionHandler(.cancel)
            return
        }
        do {
            handle = try MacUpdate.createPrivateFile(asset.name, in: destination.deletingLastPathComponent())
            completionHandler(.allow)
        } catch {
            stop(.transport(error.localizedDescription))
            completionHandler(.cancel)
        }
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard failure == nil, let handle else { return }
        received += data.count
        guard received <= asset.size else {
            stop(.wrongSize(expected: asset.size, received: received))
            return
        }
        do {
            try handle.write(contentsOf: data)
        } catch {
            stop(.transport(error.localizedDescription))
            return
        }
        hasher.update(data: data)
        if received - reported >= max(asset.size / 200, 1) || received == asset.size {
            reported = received
            progress(received)
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        try? handle?.close()
        handle = nil
        let outcome: Swift.Result<Result, Error>
        if let failure {
            outcome = .failure(failure)
        } else if let error {
            outcome = .failure(Failure.transport(error.localizedDescription))
        } else if received != asset.size {
            outcome = .failure(Failure.wrongSize(expected: asset.size, received: received))
        } else {
            outcome = .success(Result(file: destination, sha256: UpdateChecksums.hex(hasher.finalize())))
        }
        continuation?.resume(with: outcome)
        continuation = nil
    }

    private func stop(_ reason: Failure) {
        if failure == nil { failure = reason }
        task?.cancel()
    }
}
