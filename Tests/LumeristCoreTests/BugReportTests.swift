import Testing
@testable import LumeristCore

/// The Mac-only system line of "Report a problem" (the links themselves are
/// shared cases: SharedFixtureTests.bugReportLinks).
struct BugReportTests {
    @Test func macOSDescription() {
        #expect(BugReport.macOSDescription(major: 27, minor: 0, patch: 0, build: "26A123") == "macOS 27.0 (26A123)")
        #expect(BugReport.macOSDescription(major: 15, minor: 4, patch: 1, build: "24E263") == "macOS 15.4.1 (24E263)")
        #expect(BugReport.macOSDescription(major: 14, minor: 6, patch: 0, build: nil) == "macOS 14.6")
        #expect(BugReport.macOSDescription(major: 14, minor: 6, patch: 0, build: "") == "macOS 14.6")
    }

    @Test func macOSBuild() {
        #expect(BugReport.macOSBuild(fromVersionString: "Version 27.0 (Build 26A123)") == "26A123")
        #expect(BugReport.macOSBuild(fromVersionString: "Version 15.4.1 (Build 24E263a)") == "24E263a")
        #expect(BugReport.macOSBuild(fromVersionString: "Version 14.6") == nil)
    }
}
