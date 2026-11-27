import Foundation
import Network
import Testing
@testable import LumeristCore

/// Loopback tests for the command/acknowledgement client. They use ephemeral
/// ports only, never Lightroom's 50550/50551.
struct ClientTests {
    @Test(.timeLimit(.minutes(1)))
    func deliversRequestsWithIncreasingIds() async throws {
        let receiver = try LoopbackLineReceiver()
        let port = try await receiver.start()
        defer { receiver.stop() }
        let responseSender = try LoopbackResponseSender()
        let responsePort = try await responseSender.start()
        defer { responseSender.stop() }

        let states = AsyncStream.makeStream(of: LightroomCommandClient.State.self)
        let client = LightroomCommandClient(
            port: port,
            responsePort: responsePort,
            token: testToken,
            onState: { states.continuation.yield($0) }
        )
        defer {
            client.stop()
            states.continuation.finish()
        }
        client.start()
        try await waitUntilReady(states.stream)

        let first = try LightroomRequest.adjust(.develop, "Exposure", delta: 0.1)
        let firstID = try await send(first, with: client)
        let received = try RequestCodec.decodeLine(try await receiver.nextLine())
        #expect(received.token == testToken)
        #expect(received.request == first)
        #expect(received.id == firstID)

        let second = try LightroomRequest.command(.createMask, "aiSelection/sky")
        let secondID = try await send(second, with: client)
        #expect(secondID == firstID + 1)
        let decoded = try RequestCodec.decodeLine(try await receiver.nextLine())
        #expect(decoded.request == second)
        #expect(decoded.id == secondID)
    }

    @Test(.timeLimit(.minutes(1)))
    func adjustmentsReachThePlugInAsProtocolThree() async throws {
        let receiver = try LoopbackReceiver()
        let port = try await receiver.start()
        defer { receiver.stop() }
        let responseSender = try LoopbackResponseSender()
        let responsePort = try await responseSender.start()
        defer { responseSender.stop() }

        let states = AsyncStream.makeStream(of: LightroomCommandClient.State.self)
        let client = LightroomCommandClient(port: port, responsePort: responsePort, token: testToken, onState: { states.continuation.yield($0) })
        defer {
            client.stop()
            states.continuation.finish()
        }
        client.start()
        try await waitUntilReady(states.stream)

        let request = try LightroomRequest.adjust(.develop, "Exposure", delta: 0.1)
        let id = try await send(request, with: client)
        let decoded = try RequestCodec.decodeLine(try await receiver.nextMessage())
        #expect(decoded.request == request)
        #expect(decoded.id == id)
    }

    @Test(.timeLimit(.minutes(1)))
    func receivesAcksEventsAndLargeResults() async throws {
        let commandReceiver = try LoopbackReceiver()
        let commandPort = try await commandReceiver.start()
        defer { commandReceiver.stop() }

        let responseSender = try LoopbackResponseSender()
        let responsePort = try await responseSender.start()
        defer { responseSender.stop() }

        let states = AsyncStream.makeStream(of: LightroomCommandClient.State.self)
        let messages = AsyncStream.makeStream(of: LightroomMessage.self)
        let client = LightroomCommandClient(
            port: commandPort,
            responsePort: responsePort,
            token: testToken,
            onState: { states.continuation.yield($0) },
            onMessage: { messages.continuation.yield($0) }
        )
        defer {
            client.stop()
            states.continuation.finish()
            messages.continuation.finish()
        }
        client.start()

        let returnConnection = try await responseSender.nextConnection()
        try await waitUntilReady(states.stream)
        let id = try await send(.ping, with: client)
        _ = try await commandReceiver.nextMessage()

        // A preset list of more than 200 KB, split across many reads.
        let items = (0..<3_000).map { PresetItem(id: "UUID-\($0)", name: "Preset number \($0) with a long name", group: "Folder \($0 % 7)") }
        let ack = try LightroomAck(id: id, accepted: true, message: "Lumerist plug-in ready (protocol 4)")
        var payload = try MessageCodec.encodeLine(.context(LightroomContext(module: "develop", tool: "masking")))
        payload += try MessageCodec.encodeLine(.ack(ack))
        payload += try MessageCodec.encodeLine(.result(.presets(id: 99, items: items, truncated: false)))
        #expect(payload.count > 200_000)
        returnConnection.send(content: payload, completion: .contentProcessed { _ in })

        var received: [LightroomMessage] = []
        for await message in messages.stream {
            received.append(message)
            if received.count == 3 { break }
        }
        #expect(received[0] == .context(LightroomContext(module: "develop", tool: "masking")))
        #expect(received[1] == .ack(ack))
        #expect(received[2] == .result(.presets(id: 99, items: items, truncated: false)))
    }

    @Test(.timeLimit(.minutes(1)))
    func refusesToSendBeforeTheCommandChannelIsReady() async throws {
        let unusedPort = try await reserveFreePort()
        let client = LightroomCommandClient(port: unusedPort, responsePort: unusedPort, token: testToken, onState: { _ in })
        defer { client.stop() }
        client.start()

        let result = await withCheckedContinuation { continuation in
            client.send(.ping) { continuation.resume(returning: $0) }
        }
        #expect(throws: (any Error).self) { try result.get() }
    }

    @Test(.timeLimit(.minutes(1)))
    func reconnectsOnItsOwnWhenLightroomStartsListeningLater() async throws {
        let responseSender = try LoopbackResponseSender()
        let responsePort = try await responseSender.start()
        defer { responseSender.stop() }

        // Nothing listens on this port yet, like Lightroom while it rebinds.
        let commandPort = try await reserveFreePort()

        let states = AsyncStream.makeStream(of: LightroomCommandClient.State.self)
        let client = LightroomCommandClient(
            port: commandPort,
            responsePort: responsePort,
            token: testToken,
            initialRetryDelay: 0.2,
            maximumRetryDelay: 0.4,
            onState: { states.continuation.yield($0) }
        )
        defer {
            client.stop()
            states.continuation.finish()
        }
        client.start()
        try await Task.sleep(for: .milliseconds(300)) // Let the first attempt fail.

        let receiver = try LoopbackReceiver(port: commandPort)
        #expect(try await receiver.start() == commandPort)
        defer { receiver.stop() }

        try await waitUntilReady(states.stream)
        try await send(.ping, with: client)
        #expect(try RequestCodec.decodeLine(await receiver.nextMessage()).request == .ping)
    }

    @Test(.timeLimit(.minutes(1)))
    func retryingOneChannelKeepsTheHealthyChannelOpen() async throws {
        let connections = ConnectionCounter()
        let commandListener = try NWListener(using: loopbackParameters(), on: .any)
        commandListener.newConnectionHandler = { connection in
            connections.increment()
            connection.start(queue: .global(qos: .userInitiated))
        }
        let commandPort = try await startListener(commandListener)
        defer { commandListener.cancel() }

        // The response endpoint stays closed, so that channel keeps retrying.
        let responsePort = try await reserveFreePort()
        let client = LightroomCommandClient(
            port: commandPort,
            responsePort: responsePort,
            token: testToken,
            initialRetryDelay: 0.1,
            maximumRetryDelay: 0.1,
            onState: { _ in }
        )
        defer { client.stop() }
        client.start()
        try await Task.sleep(for: .milliseconds(900))

        #expect(connections.value == 1)
    }

    @Test(.timeLimit(.minutes(1)))
    func stopEndsTheRetries() async throws {
        let unusedPort = try await reserveFreePort()
        let states = AsyncStream.makeStream(of: LightroomCommandClient.State.self)
        let client = LightroomCommandClient(
            port: unusedPort,
            responsePort: unusedPort,
            token: testToken,
            initialRetryDelay: 0.1,
            maximumRetryDelay: 0.1,
            onState: { states.continuation.yield($0) }
        )
        client.start()
        try await Task.sleep(for: .milliseconds(400))
        client.stop()

        // Drain what was emitted before stop, then make sure nothing follows.
        try await Task.sleep(for: .milliseconds(100))
        let receiver = try LoopbackReceiver(port: unusedPort)
        _ = try await receiver.start()
        defer { receiver.stop() }
        try await Task.sleep(for: .milliseconds(600))
        states.continuation.finish()

        var sawReady = false
        for await state in states.stream where state == .ready {
            sawReady = true
        }
        #expect(!sawReady)
    }
}

extension ClientTests {
    @Test(.timeLimit(.minutes(1)))
    func aReconnectDuringARetryKeepsTheNewConnection() async throws {
        let commandReceiver = try LoopbackReceiver()
        let commandPort = try await commandReceiver.start()
        defer { commandReceiver.stop() }
        let responsePort = try await reserveFreePort()

        let log = StateLog()
        let client = LightroomCommandClient(
            port: commandPort,
            responsePort: responsePort,
            token: testToken,
            initialRetryDelay: 1.5,
            maximumRetryDelay: 1.5,
            onState: { log.append($0) }
        )
        defer { client.stop() }
        client.start()
        // Nothing listens on the response port yet: that channel fails first.
        try await log.waitFor { if case .waiting(let reason) = $0 { reason.hasPrefix("response channel") } else { false } }

        // The plug-in starts listening and the user presses Reconnect before the retry is due.
        let connections = ConnectionCounter()
        let responseListener = try NWListener(using: loopbackParameters(), on: NWEndpoint.Port(rawValue: responsePort)!)
        responseListener.newConnectionHandler = { connection in
            connections.increment()
            connection.start(queue: .global(qos: .userInitiated))
        }
        _ = try await startListener(responseListener)
        defer { responseListener.cancel() }
        client.reconnect()
        try await log.waitFor { $0 == .ready }
        let statesWhenReady = log.count

        // The retry for the failed attempt comes due and must leave the new connection alone.
        try await Task.sleep(for: .milliseconds(2_500))
        #expect(connections.value == 1)
        #expect(log.count == statesWhenReady)
    }

    @Test(.timeLimit(.minutes(1)))
    func nothingIsDeliveredOnceStopReturns() async throws {
        let unusedPort = try await reserveFreePort()
        let log = StateLog()
        let client = LightroomCommandClient(
            port: unusedPort,
            responsePort: unusedPort,
            token: testToken,
            initialRetryDelay: 0.02,
            maximumRetryDelay: 0.02,
            onState: { log.append($0) }
        )
        client.start()
        try await log.waitFor(count: 6)
        client.stop()
        // A callback already running when stop() was called may still finish.
        try await Task.sleep(for: .milliseconds(20))
        let countAfterStop = log.count
        try await Task.sleep(for: .milliseconds(400))
        #expect(log.count == countAfterStop)
    }
}

/// States in arrival order, readable from the test while the client appends.
private final class StateLog: @unchecked Sendable {
    private let lock = NSLock()
    private var states: [LightroomCommandClient.State] = []

    func append(_ state: LightroomCommandClient.State) {
        lock.lock()
        states.append(state)
        lock.unlock()
    }

    var count: Int {
        lock.lock()
        defer { lock.unlock() }
        return states.count
    }

    private var all: [LightroomCommandClient.State] {
        lock.lock()
        defer { lock.unlock() }
        return states
    }

    func waitFor(timeout: Duration = .seconds(10), _ matches: (LightroomCommandClient.State) -> Bool) async throws {
        let deadline = ContinuousClock.now + timeout
        while !all.contains(where: matches) {
            guard ContinuousClock.now < deadline else { throw ReadyTimeout() }
            try await Task.sleep(for: .milliseconds(20))
        }
    }

    func waitFor(count: Int, timeout: Duration = .seconds(10)) async throws {
        let deadline = ContinuousClock.now + timeout
        while self.count < count {
            guard ContinuousClock.now < deadline else { throw ReadyTimeout() }
            try await Task.sleep(for: .milliseconds(20))
        }
    }
}

// MARK: - Helpers

private struct ReadyTimeout: Error {}

private func waitUntilReady(
    _ states: AsyncStream<LightroomCommandClient.State>,
    timeout: Duration = .seconds(10)
) async throws {
    try await withThrowingTaskGroup(of: Bool.self) { group in
        group.addTask {
            for await state in states where state == .ready {
                return true
            }
            return false
        }
        group.addTask {
            try await Task.sleep(for: timeout)
            return false
        }
        let becameReady = try await group.next() ?? false
        group.cancelAll()
        guard becameReady else { throw ReadyTimeout() }
    }
}

@discardableResult
private func send(_ request: LightroomRequest, with client: LightroomCommandClient) async throws -> Int {
    let box = IDBox()
    let result = await withCheckedContinuation { continuation in
        box.id = client.send(request) { continuation.resume(returning: $0) }
    }
    try result.get()
    return try #require(box.id)
}

private final class IDBox: @unchecked Sendable {
    var id: Int?
}

/// Finds a port that is free right now by listening briefly on an ephemeral one.
private func reserveFreePort() async throws -> UInt16 {
    let probe = try LoopbackReceiver()
    let port = try await probe.start()
    probe.stop()
    try await Task.sleep(for: .milliseconds(50))
    return port
}

private func loopbackParameters() -> NWParameters {
    let parameters = NWParameters.tcp
    parameters.allowLocalEndpointReuse = true
    return parameters
}

private func startListener(_ listener: NWListener) async throws -> UInt16 {
    try await withCheckedThrowingContinuation { continuation in
        let resumed = ResumeOnce()
        listener.stateUpdateHandler = { [listener] state in
            switch state {
            case .ready:
                if resumed.claim() { continuation.resume(returning: listener.port!.rawValue) }
            case .failed(let error):
                if resumed.claim() { continuation.resume(throwing: error) }
            case .cancelled:
                if resumed.claim() { continuation.resume(throwing: CancellationError()) }
            default:
                break
            }
        }
        listener.start(queue: .global(qos: .userInitiated))
    }
}

private final class ResumeOnce: @unchecked Sendable {
    private let lock = NSLock()
    private var done = false

    func claim() -> Bool {
        lock.lock()
        defer { lock.unlock() }
        guard !done else { return false }
        done = true
        return true
    }
}

private final class LoopbackReceiver: @unchecked Sendable {
    private let listener: NWListener
    private let messages: AsyncStream<Data>
    private let continuation: AsyncStream<Data>.Continuation

    init(port: UInt16? = nil) throws {
        let endpointPort = port.flatMap { NWEndpoint.Port(rawValue: $0) } ?? .any
        let listener = try NWListener(using: loopbackParameters(), on: endpointPort)
        let stream = AsyncStream.makeStream(of: Data.self)
        self.listener = listener
        self.messages = stream.stream
        self.continuation = stream.continuation
        listener.newConnectionHandler = { [continuation = stream.continuation] connection in
            connection.start(queue: .global(qos: .userInitiated))
            connection.receive(minimumIncompleteLength: 1, maximumLength: 16_384) { data, _, _, _ in
                if let data { continuation.yield(data) }
                connection.cancel()
            }
        }
    }

    deinit { stop() }

    func start() async throws -> UInt16 {
        try await startListener(listener)
    }

    func nextMessage() async throws -> Data {
        for await message in messages {
            return message
        }
        throw ReceiverError.finishedWithoutMessage
    }

    func stop() {
        listener.cancel()
        continuation.finish()
    }

    private enum ReceiverError: Error {
        case finishedWithoutMessage
    }
}

private final class LoopbackResponseSender: @unchecked Sendable {
    private let listener: NWListener
    private let connections: AsyncStream<NWConnection>
    private let continuation: AsyncStream<NWConnection>.Continuation

    init() throws {
        listener = try NWListener(using: loopbackParameters(), on: .any)
        let stream = AsyncStream.makeStream(of: NWConnection.self)
        connections = stream.stream
        continuation = stream.continuation
        listener.newConnectionHandler = { [continuation = stream.continuation] connection in
            connection.start(queue: .global(qos: .userInitiated))
            continuation.yield(connection)
        }
    }

    deinit { stop() }

    func start() async throws -> UInt16 {
        try await startListener(listener)
    }

    func nextConnection() async throws -> NWConnection {
        for await connection in connections {
            return connection
        }
        throw ResponseSenderError.finishedWithoutConnection
    }

    func stop() {
        listener.cancel()
        continuation.finish()
    }

    private enum ResponseSenderError: Error {
        case finishedWithoutConnection
    }
}

private final class ConnectionCounter: @unchecked Sendable {
    private let lock = NSLock()
    private var count = 0

    func increment() {
        lock.lock()
        count += 1
        lock.unlock()
    }

    var value: Int {
        lock.lock()
        defer { lock.unlock() }
        return count
    }
}

/// Keeps the connection open and yields every received line (newline kept).
private final class LoopbackLineReceiver: @unchecked Sendable {
    private let listener: NWListener
    private let lines: AsyncStream<Data>
    private let continuation: AsyncStream<Data>.Continuation

    init() throws {
        listener = try NWListener(using: loopbackParameters(), on: .any)
        let stream = AsyncStream.makeStream(of: Data.self)
        lines = stream.stream
        continuation = stream.continuation
        listener.newConnectionHandler = { [continuation = stream.continuation] connection in
            connection.start(queue: .global(qos: .userInitiated))
            let buffer = LineBuffer()
            @Sendable func read() {
                connection.receive(minimumIncompleteLength: 1, maximumLength: 65_536) { data, _, isComplete, error in
                    if let data {
                        for line in buffer.append(data) { continuation.yield(line) }
                    }
                    if !isComplete && error == nil { read() }
                }
            }
            read()
        }
    }

    deinit { stop() }

    func start() async throws -> UInt16 {
        try await startListener(listener)
    }

    func nextLine() async throws -> Data {
        for await line in lines {
            return line
        }
        throw CancellationError()
    }

    func stop() {
        listener.cancel()
        continuation.finish()
    }
}

private final class LineBuffer: @unchecked Sendable {
    private let lock = NSLock()
    private var data = Data()

    func append(_ chunk: Data) -> [Data] {
        lock.lock()
        defer { lock.unlock() }
        data.append(chunk)
        var lines: [Data] = []
        while let newline = data.firstIndex(of: 0x0A) {
            lines.append(Data(data[...newline]))
            data.removeSubrange(...newline)
        }
        return lines
    }
}
