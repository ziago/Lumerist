import Testing
@testable import LumeristCore

/// The shapes measured in Lightroom Classic 16.0.1 on 27 Nov 2026.
struct ImportWindowRuleTests {
    private static func catalog(_ subrole: String = "AXStandardWindow", focused: Bool = false) -> WindowShape {
        WindowShape(subrole: subrole, hasTitle: true, width: 1728, height: 1084, isFocused: focused)
    }

    private static func importWindow(focused: Bool) -> WindowShape {
        WindowShape(subrole: "AXUnknown", hasTitle: false, width: 1728, height: 1052, isFocused: focused)
    }

    /// Small untitled windows Lightroom always has (tooltips, palettes).
    private static let clutter = [
        WindowShape(subrole: "AXUnknown", hasTitle: false, width: 300, height: 200),
        WindowShape(subrole: "AXHelpTag", hasTitle: false, width: 120, height: 20),
        WindowShape(role: "AXUnknown", subrole: nil, hasTitle: false, width: 1728, height: 1084),
    ]

    @Test func theCatalogWindowAloneIsNotImport() {
        #expect(!ImportWindowRule.isImportWindowOpen([Self.catalog(focused: true)] + Self.clutter))
        #expect(!ImportWindowRule.isImportWindowOpen([]))
    }

    @Test func theImportWindowIsRecognisedFromOpeningToClosing() {
        // Open: focused, the catalog window turned into a dialog.
        #expect(ImportWindowRule.isImportWindowOpen([Self.catalog("AXDialog"), Self.importWindow(focused: true)] + Self.clutter))
        // The first 2 s: the catalog window still has the focus, but is already a dialog.
        #expect(ImportWindowRule.isImportWindowOpen([Self.catalog("AXDialog", focused: true), Self.importWindow(focused: false)]))
        // Closing: the catalog window is a standard window again, the Import window still focused.
        #expect(ImportWindowRule.isImportWindowOpen([Self.catalog(), Self.importWindow(focused: true)]))
        // Closed.
        #expect(!ImportWindowRule.isImportWindowOpen([Self.catalog(focused: true), Self.importWindow(focused: false)]))
    }

    @Test func exportAndPreferencesAreNotImport() {
        let export = WindowShape(subrole: "AXDialog", hasTitle: true, width: 1275, height: 883, isFocused: true)
        #expect(!ImportWindowRule.isImportWindowOpen([Self.catalog(), export] + Self.clutter))
        #expect(!ImportWindowRule.isImportWindowOpen([Self.catalog("AXDialog"), export] + Self.clutter))
        let preferences = WindowShape(subrole: "AXStandardWindow", hasTitle: true, width: 1246, height: 841, isFocused: true)
        #expect(!ImportWindowRule.isImportWindowOpen([Self.catalog(), preferences]))
    }

    @Test func aWindowMuchSmallerThanTheCatalogIsNotImport() {
        let narrow = WindowShape(subrole: "AXUnknown", hasTitle: false, width: 1500, height: 1052, isFocused: true)
        let short = WindowShape(subrole: "AXUnknown", hasTitle: false, width: 1728, height: 800, isFocused: true)
        let helpTag = WindowShape(subrole: "AXHelpTag", hasTitle: false, width: 1728, height: 1084, isFocused: true)
        for window in [narrow, short, helpTag] {
            #expect(!ImportWindowRule.isImportWindowOpen([Self.catalog("AXDialog"), window]))
        }
        // Without a titled window there is no catalog window to compare with.
        #expect(!ImportWindowRule.isImportWindowOpen([Self.importWindow(focused: true)]))
    }
}
