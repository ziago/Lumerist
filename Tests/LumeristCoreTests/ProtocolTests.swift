import Foundation
import Testing
@testable import LumeristCore

/// Protocol 3 requests: validation against the catalog and the wire format.
struct ProtocolTests {
    @Test func encodesAnAdjustmentWithSortedKeysAndANewline() throws {
        let request = try LightroomRequest.adjust(.develop, "Exposure", delta: 0.05)
        let line = try RequestCodec.encodeLine(request, id: 7)
        #expect(String(decoding: line, as: UTF8.self)
            == #"{"delta":0.05,"id":7,"kind":"adjust","parameter":"Exposure","scope":"develop","version":3}"# + "\n")
    }

    @Test func roundTripsEveryKind() throws {
        let requests: [LightroomRequest] = [
            .ping,
            try .adjust(.library, "Exposure", delta: -0.3),
            try .reset(.develop, "lumerist.cropZoom"),
            try .gesture(.begin, "local_Exposure"),
            try .gesture(.end, nil),
            try .command(.createMask, "aiSelection/subject"),
            try .command(.undo),
            .query(.presets),
        ]
        for (index, request) in requests.enumerated() {
            let decoded = try RequestCodec.decodeLine(try RequestCodec.encodeLine(request, id: index))
            #expect(decoded.id == index)
            #expect(decoded.request == request)
        }
    }

    @Test func slashesInArgumentsAreNotEscaped() throws {
        let line = try RequestCodec.encodeLine(try .command(.addToMask, "rangeMask/luminance"), id: 1)
        #expect(String(decoding: line, as: UTF8.self).contains(#""argument":"rangeMask/luminance""#))
    }

    @Test func adjustmentsAreLimitedByTheCatalog() throws {
        _ = try LightroomRequest.adjust(.develop, "Exposure", delta: 1)
        #expect(throws: LightroomRequest.ValidationError.invalidDelta(ParameterKey(scope: .develop, id: "Exposure"), limit: 1)) {
            _ = try LightroomRequest.adjust(.develop, "Exposure", delta: 1.01)
        }
        _ = try LightroomRequest.adjust(.develop, "Contrast", delta: -20)
        #expect(throws: (any Error).self) { _ = try LightroomRequest.adjust(.develop, "Contrast", delta: 20.5) }
        #expect(throws: (any Error).self) { _ = try LightroomRequest.adjust(.develop, "Exposure", delta: .nan) }
        #expect(throws: (any Error).self) { _ = try LightroomRequest.adjust(.develop, "Exposure", delta: .infinity) }
    }

    @Test func parametersMustExistInTheirScope() throws {
        _ = try LightroomRequest.adjust(.develop, "SplitToningShadowHue", delta: 20)
        _ = try LightroomRequest.adjust(.library, "Clarity", delta: 1)
        #expect(throws: LightroomRequest.ValidationError.unknownParameter(ParameterKey(scope: .library, id: "Dehaze"))) {
            _ = try LightroomRequest.adjust(.library, "Dehaze", delta: 1)
        }
        #expect(throws: (any Error).self) { _ = try LightroomRequest.adjust(.develop, "NotAParameter", delta: 1) }
    }

    @Test func resetIsOnlyForDevelop() throws {
        _ = try LightroomRequest.reset(.develop, "Exposure")
        #expect(throws: LightroomRequest.ValidationError.resetNotAvailable(.library)) {
            _ = try LightroomRequest.reset(.library, "Exposure")
        }
    }

    @Test func gestureBeginNeedsAParameter() throws {
        _ = try LightroomRequest.gesture(.end, nil)
        #expect(throws: LightroomRequest.ValidationError.missingGestureParameter) {
            _ = try LightroomRequest.gesture(.begin, nil)
        }
        #expect(throws: (any Error).self) { _ = try LightroomRequest.gesture(.begin, "Nope") }
    }

    @Test func commandArgumentsAreChecked() throws {
        let good: [(LightroomCommandName, String?)] = [
            (.undo, nil), (.rating, "0"), (.rating, "5"), (.colorLabel, "none"), (.switchModule, "library"),
            (.selectTool, "masking"), (.revealPanel, "Exposure"), (.createMask, "aiSelection/subject"),
            (.addToMask, "brush"), (.applyPreset, "A1B2-C3D4"),
        ]
        for (name, argument) in good {
            _ = try LightroomRequest.command(name, argument)
        }
        let bad: [(LightroomCommandName, String?)] = [
            (.undo, "x"), (.rating, "6"), (.rating, "05"), (.rating, nil), (.colorLabel, "pink"),
            (.switchModule, "photoshop"), (.selectTool, "lasso"), (.revealPanel, "Nope"),
            (.createMask, "foo/bar"), (.createMask, "brush/sky/x"), (.applyPreset, "bad uuid!"), (.applyPreset, ""),
        ]
        for (name, argument) in bad {
            #expect(throws: LightroomRequest.ValidationError.invalidArgument(name, argument)) {
                _ = try LightroomRequest.command(name, argument)
            }
        }
    }

    @Test func rejectsOtherProtocolVersionsOnDecode() {
        let line = #"{"id":1,"kind":"ping","version":2}"#
        #expect(throws: RequestCodec.DecodingError.unsupportedVersion(2)) {
            _ = try RequestCodec.decodeLine(Data(line.utf8))
        }
    }

    @Test func everyCatalogEntryIsAddressable() throws {
        for descriptor in ParameterCatalog.all {
            let request = try LightroomRequest.adjust(descriptor.scope, descriptor.id, delta: descriptor.maximumDelta)
            #expect(request.parameterKey == descriptor.key)
        }
    }
}
