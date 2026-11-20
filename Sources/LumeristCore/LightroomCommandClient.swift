import Foundation
import Network

/// A reconnectable pair of loopback-only connections to the Lightroom plug-in.
/// Commands travel to the plug-in and short acknowledgements return on a
/// separate port, so a completed TCP write is not mistaken for a completed
/// Lightroom action.
///
/// Lightroom's `LrSocket` endpoints stop listening after a short timeout and
/// then rebind, so the client retries on its own with a capped backoff until
/// `stop()` is called.
///
/// Protocol 3: every request gets an increasing id, echoed by the plug-in's
/// `ack`/`result`; `onMessage` receives acks, context events and results.
public final class LightroomCommandClient: @unchecked Sendable {
    public enum State: Sendable, Equatable {
        case ready
        case waiting(String)
    }

    private enum Channel: CaseIterable {
        case command
        case response

        var label: String {
            switch self {
            case .command: "command"
            case .response: "response"
            }
        }
    }

    private let lock = NSLock()
    private let commandPort: NWEndpoint.Port
    private let responsePort: NWEndpoint.Port
    private let initialRetryDelay: TimeInterval
    private let maximumRetryDelay: TimeInterval
    private let onState: @Sendable (State) -> Void
    private let onMessage: @Sendable (LightroomMessage) -> Void
    private var nextRequestID = 1
    private var commandConnection: NWConnection?
    private var responseConnection: NWConnection?
    private var commandIsReady = false
    private var responseIsReady = false
    private var responseBuffer = Data()
    private var isStopped = true
    /// Bumped whenever a channel's connection is replaced, so a pending retry
    /// for an older connection does nothing.
    private var generations: [Channel: Int] = [.command: 0, .response: 0]
    private var retryScheduled: Set<Channel> = []
    private var retryDelay: TimeInterval
    /// Bumped by `stop()`, so a callback queued before it is dropped.
    private var epoch = 0
    /// State changes and messages reach the app one at a time, in the order of
    /// the lock sections that produced them (connections report on their own
    /// threads, so calling out after unlocking could swap two of them).
    private let delivery = DispatchQueue(label: "org.lumerist.client.delivery", qos: .userInitiated)

    private enum Delivery: Sendable {
        case state(State)
        case message(LightroomMessage)
    }

    public init(
        port: UInt16,
        responsePort: UInt16 = 50_551,
        initialRetryDelay: TimeInterval = 1,
        maximumRetryDelay: TimeInterval = 5,
        onState: @escaping @Sendable (State) -> Void,
        onMessage: @escaping @Sendable (LightroomMessage) -> Void = { _ in }
    ) {
        self.commandPort = NWEndpoint.Port(rawValue: port)!
        self.responsePort = NWEndpoint.Port(rawValue: responsePort)!
        self.initialRetryDelay = initialRetryDelay
        self.maximumRetryDelay = maximumRetryDelay
        self.retryDelay = initialRetryDelay
        self.onState = onState
        self.onMessage = onMessage
    }

    public func start() {
        lock.lock()
        isStopped = false
        retryDelay = initialRetryDelay
        lock.unlock()
        reconnect()
    }

    /// Replaces both connections (the manual "Reconnect" button).
    public func reconnect() {
        for channel in Channel.allCases {
            connect(channel)
        }
    }

    /// Replaces one channel only, so a healthy channel is never torn down
    /// because the other one is still waiting for Lightroom.
    /// - Parameter generation: for a retry, the generation it was scheduled
    ///   for; a channel replaced since (Reconnect) is left alone.
    private func connect(_ channel: Channel, onlyIfGeneration generation: Int? = nil) {
        lock.lock()
        guard !isStopped, generation.map({ $0 == generations[channel, default: 0] }) ?? true else {
            lock.unlock()
            return
        }
        let port = channel == .command ? commandPort : responsePort
        let connection = NWConnection(host: "127.0.0.1", port: port, using: .tcp)
        let previous: NWConnection?
        switch channel {
        case .command:
            previous = commandConnection
            commandConnection = connection
            commandIsReady = false
        case .response:
            previous = responseConnection
            responseConnection = connection
            responseIsReady = false
            responseBuffer.removeAll(keepingCapacity: true)
        }
        generations[channel, default: 0] += 1
        retryScheduled.remove(channel)
        lock.unlock()

        previous?.cancel()
        connection.stateUpdateHandler = { [weak self, weak connection] state in
            guard let connection else { return }
            self?.handleState(state, for: channel, connection: connection)
        }
        connection.start(queue: .global(qos: .userInitiated))
        switch channel {
        case .command: watchForClose(connection)
        case .response: receiveResponse(on: connection)
        }
    }

    /// Once `stop()` returns, nothing queued before it is delivered, and nothing new is queued.
    public func stop() {
        lock.lock()
        isStopped = true
        epoch += 1
        for channel in Channel.allCases {
            generations[channel, default: 0] += 1
        }
        retryScheduled.removeAll()
        let command = commandConnection
        let response = responseConnection
        commandConnection = nil
        responseConnection = nil
        commandIsReady = false
        responseIsReady = false
        responseBuffer.removeAll(keepingCapacity: true)
        lock.unlock()
        command?.cancel()
        response?.cancel()
    }

    /// Sends a protocol-3 request and returns the id it was given (nil when the
    /// command channel is not connected; `completion` then reports the error).
    /// `completion` reports the TCP write only; Lightroom's verdict arrives as
    /// an `ack` (or `result`) with the same id through `onMessage`.
    @discardableResult
    public func send(
        _ request: LightroomRequest,
        completion: @escaping @Sendable (Result<Void, Error>) -> Void = { _ in }
    ) -> Int? {
        lock.lock()
        let connection = commandIsReady ? commandConnection : nil
        let id = nextRequestID
        if connection != nil {
            nextRequestID += 1
        }
        lock.unlock()
        guard let connection else {
            completion(.failure(ClientError.notConnected))
            return nil
        }
        do {
            let line = try RequestCodec.encodeLine(request, id: id)
            connection.send(content: line, completion: .contentProcessed { error in
                if let error {
                    completion(.failure(error))
                } else {
                    completion(.success(()))
                }
            })
            return id
        } catch {
            completion(.failure(error))
            return nil
        }
    }

    private func isCurrent(_ connection: NWConnection, _ channel: Channel) -> Bool {
        switch channel {
        case .command: commandConnection === connection
        case .response: responseConnection === connection
        }
    }

    private func handleState(_ state: NWConnection.State, for channel: Channel, connection: NWConnection) {
        lock.lock()
        defer { lock.unlock() }
        guard isCurrent(connection, channel) else { return }

        switch state {
        case .ready:
            switch channel {
            case .command: commandIsReady = true
            case .response: responseIsReady = true
            }
            if commandIsReady && responseIsReady {
                retryDelay = initialRetryDelay
                deliverLocked(.state(.ready), from: connection, channel)
            }
        case .failed(let error), .waiting(let error):
            channelDownLocked(connection, channel, reason: "\(channel.label) channel: \(error.localizedDescription)")
        case .cancelled:
            channelDownLocked(connection, channel, reason: "\(channel.label) channel is closed")
        default:
            break
        }
    }

    /// Call with `lock` held.
    private func markNotReady(_ channel: Channel) {
        switch channel {
        case .command: commandIsReady = false
        case .response: responseIsReady = false
        }
    }

    /// Lightroom closes a socket when it rebinds; the command channel never
    /// receives data, so reading from it is how a close is noticed.
    private func watchForClose(_ connection: NWConnection) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 1_024) { [weak self, weak connection] _, _, isComplete, error in
            guard let self, let connection else { return }
            if isComplete || error != nil {
                self.channelClosed(connection, .command)
            } else {
                self.watchForClose(connection)
            }
        }
    }

    private func receiveResponse(on connection: NWConnection) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 65_536) { [weak self, weak connection] data, _, isComplete, error in
            guard let self, let connection else { return }
            if let data {
                self.consumeResponse(data, from: connection)
            }
            if isComplete || error != nil {
                self.channelClosed(connection, .response)
                return
            }
            self.receiveResponse(on: connection)
        }
    }

    private func channelClosed(_ connection: NWConnection, _ channel: Channel) {
        lock.lock()
        defer { lock.unlock() }
        guard isCurrent(connection, channel) else { return }
        channelDownLocked(connection, channel, reason: "\(channel.label) channel was closed by Lightroom")
    }

    /// Call with `lock` held. Reports the channel down and schedules its retry
    /// in one lock section, so the retry belongs to this connection's
    /// generation: a Reconnect that happens meanwhile is never torn down by it.
    private func channelDownLocked(_ connection: NWConnection, _ channel: Channel, reason: String) {
        markNotReady(channel)
        deliverLocked(.state(.waiting(reason)), from: connection, channel)
        guard !isStopped, !retryScheduled.contains(channel) else { return }
        retryScheduled.insert(channel)
        let delay = retryDelay
        retryDelay = min(retryDelay * 2, maximumRetryDelay)
        let scheduledGeneration = generations[channel, default: 0]
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + delay) { [weak self] in
            self?.connect(channel, onlyIfGeneration: scheduledGeneration)
        }
    }

    /// Call with `lock` held. Queued under the lock, so callbacks keep the
    /// order in which they happened; dropped when they arrive after `stop()`
    /// or after their connection was replaced.
    private func deliverLocked(_ item: Delivery, from connection: NWConnection, _ channel: Channel) {
        let queuedEpoch = epoch
        delivery.async { [weak self, weak connection] in
            guard let self, let connection else { return }
            self.lock.lock()
            let isLive = queuedEpoch == self.epoch && !self.isStopped && self.isCurrent(connection, channel)
            self.lock.unlock()
            guard isLive else { return }
            switch item {
            case .state(let state): self.onState(state)
            case .message(let message): self.onMessage(message)
            }
        }
    }

    private func consumeResponse(_ data: Data, from connection: NWConnection) {
        var lines: [Data] = []
        lock.lock()
        guard responseConnection === connection else {
            lock.unlock()
            return
        }
        responseBuffer.append(data)
        while let newline = responseBuffer.firstIndex(of: 0x0A) {
            lines.append(Data(responseBuffer[..<newline]))
            responseBuffer.removeSubrange(...newline)
        }
        // A partial line longer than any valid message is dropped.
        let oversized = responseBuffer.count > MessageCodec.maximumLineBytes
        if oversized {
            responseBuffer.removeAll(keepingCapacity: false)
        }
        lock.unlock()

        // Decoded outside the lock; this connection's reads arrive one at a
        // time, so its lines keep their order.
        var decoded: [Delivery] = []
        for line in lines where !line.isEmpty {
            do {
                guard let message = try MessageCodec.decodeLine(line) else { continue }
                decoded.append(.message(message))
            } catch {
                decoded.append(.state(.waiting("response channel returned invalid data: \(error.localizedDescription)")))
            }
        }
        if oversized {
            decoded.append(.state(.waiting("response channel sent an oversized message")))
        }
        guard !decoded.isEmpty else { return }
        lock.lock()
        defer { lock.unlock() }
        guard responseConnection === connection else { return }
        for item in decoded {
            deliverLocked(item, from: connection, .response)
        }
    }

    private enum ClientError: LocalizedError {
        case notConnected

        var errorDescription: String? { "The companion is not connected to the Lumerist plug-in." }
    }
}
