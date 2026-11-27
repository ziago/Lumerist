import Foundation
import Testing
@testable import LumeristCore

/// Protocol 3 messages from the plug-in: acks, context events, results.
struct MessageTests {
    private func decode(_ line: String) throws -> LightroomMessage? {
        try MessageCodec.decodeLine(Data((line + "\n").utf8))
    }

    @Test func decodesTheAckThePlugInWrites() throws {
        let line = #"{"accepted":true,"id":12,"kind":"ack","maximum":50000,"message":"accepted","minimum":2000,"parameter":"Temperature","scope":"develop","unit":"K","value":5100,"version":4}"#
        guard case .ack(let ack)? = try decode(line) else {
            Issue.record("not an ack")
            return
        }
        #expect(ack.id == 12)
        #expect(ack.accepted)
        #expect(ack.parameter == "Temperature")
        #expect(ack.scope == .develop)
        #expect(ack.value == 5100)
        #expect(ack.minimum == 2000)
        #expect(ack.maximum == 50000)
        #expect(ack.unit == "K")
        #expect(ack.parameterKey == ParameterKey(scope: .develop, id: "Temperature"))
    }

    @Test func rejectedAckWithoutIdOrValues() throws {
        guard case .ack(let ack)? = try decode(#"{"accepted":false,"kind":"ack","message":"malformed JSON","version":4}"#) else {
            Issue.record("not an ack")
            return
        }
        #expect(ack.id == nil)
        #expect(!ack.accepted)
        #expect(ack.value == nil)
    }

    @Test func maskAckCarriesTheLocalParameter() throws {
        let line = #"{"accepted":true,"id":3,"kind":"ack","maximum":4,"message":"accepted","minimum":-4,"parameter":"local_Exposure","scope":"develop","unit":"EV","value":0.25,"version":4}"#
        guard case .ack(let ack)? = try decode(line) else {
            Issue.record("not an ack")
            return
        }
        #expect(ack.parameter == "local_Exposure")
        #expect(ack.value == 0.25)
    }

    @Test func decodesTheContextEvent() throws {
        let message = try decode(#"{"kind":"event","module":"develop","name":"context","tool":"masking","version":4}"#)
        #expect(message == .context(LightroomContext(module: "develop", tool: "masking")))
        guard case .context(let context)? = message else { return }
        #expect(context.isMasking)
    }

    @Test func decodesResults() throws {
        let presets = try decode(#"{"id":4,"items":[{"group":"Favourites","id":"A1-B2","name":"Warm \"Film\""}],"kind":"result","name":"presets","version":4}"#)
        #expect(presets == .result(.presets(id: 4, items: [PresetItem(id: "A1-B2", name: #"Warm "Film""#, group: "Favourites")], truncated: false)))
        let catalog = try decode(#"{"id":5,"kind":"result","name":"catalog","skipped":["local_Amount"],"unsupported":["LensBlurAmount"],"version":4}"#)
        #expect(catalog == .result(.catalog(id: 5, unsupported: ["LensBlurAmount"], skipped: ["local_Amount"])))
        let context = try decode(#"{"id":6,"kind":"result","module":"library","name":"context","tool":"","version":4}"#)
        #expect(context == .result(.context(id: 6, LightroomContext(module: "library", tool: ""))))
    }

    @Test func unknownKindsEventsAndResultsAreIgnored() throws {
        #expect(try decode(#"{"kind":"telemetry","version":4}"#) == nil)
        #expect(try decode(#"{"kind":"event","name":"weather","version":4}"#) == nil)
        #expect(try decode(#"{"id":1,"kind":"result","name":"secrets","version":4}"#) == nil)
        #expect(try decode(#"{"accepted":true,"future":[1,2],"kind":"ack","version":4}"#) != nil)
    }

    @Test func otherVersionsAndMalformedLinesThrow() {
        #expect(throws: MessageCodec.ValidationError.unsupportedVersion(2)) {
            _ = try decode(#"{"accepted":true,"action":"ping","message":"x","version":2}"#)
        }
        #expect(throws: MessageCodec.ValidationError.malformed) {
            _ = try decode(#"{"kind":"ack","version":4}"#)
        }
        #expect(throws: (any Error).self) { _ = try decode("not json") }
    }

    @Test func ackValidation() throws {
        #expect(throws: MessageCodec.ValidationError.invalidNumber) {
            _ = try LightroomAck(id: 1, accepted: true, value: .nan)
        }
        #expect(throws: MessageCodec.ValidationError.unitTooLong) {
            _ = try LightroomAck(id: 1, accepted: true, unit: "123456789")
        }
        #expect(throws: MessageCodec.ValidationError.messageTooLong) {
            _ = try LightroomAck(id: 1, accepted: false, message: String(repeating: "x", count: 1_025))
        }
        _ = try LightroomAck(id: 1, accepted: true, unit: "12345678")
    }

    @Test func messagesRoundTrip() throws {
        let messages: [LightroomMessage] = [
            .ack(try LightroomAck(id: 9, accepted: true, message: "accepted", parameter: "Clarity", scope: .library,
                                  value: 12, minimum: -100, maximum: 100, unit: "pt")),
            .context(LightroomContext(module: "develop", tool: "crop")),
            .result(.presets(id: 2, items: [PresetItem(id: "X", name: "Y", group: "Z")], truncated: true)),
            .result(.catalog(id: 3, unsupported: [], skipped: [])),
        ]
        for message in messages {
            #expect(try MessageCodec.decodeLine(try MessageCodec.encodeLine(message)) == message)
        }
    }
}
