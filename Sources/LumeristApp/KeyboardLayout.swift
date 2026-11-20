import AppKit
import Carbon.HIToolbox
import Foundation
import LumeristCore

/// Reads the active keyboard layout so key names match what is printed on
/// the user's keys (AZERTY, QWERTZ, Italian…), and finds the physical key
/// that types a given character (for Lightroom's own shortcuts such as ⌘Z).
/// Tables are rebuilt on the main thread whenever the input source changes.
@MainActor
enum KeyboardLayout {
    /// Immutable snapshots swapped under a lock, so a layout change on the
    /// main thread never races a reader on another thread.
    private struct Tables: Sendable {
        var names: [Int64: String] = [:]
        var keyCodesByCharacter: [String: Int64] = [:]
    }
    private nonisolated static let lock = NSLock()
    private nonisolated(unsafe) static var tables = Tables()
    private static var observer: NSObjectProtocol?

    private nonisolated static var current: Tables {
        lock.lock()
        defer { lock.unlock() }
        return tables
    }

    /// Builds the tables and installs the layout-aware names into `KeyNames`.
    static func install() {
        rebuild()
        KeyNames.layoutName = { keyCode in KeyboardLayout.current.names[keyCode] }
        guard observer == nil else { return }
        observer = DistributedNotificationCenter.default().addObserver(
            forName: NSNotification.Name(kTISNotifySelectedKeyboardInputSourceChanged as String),
            object: nil,
            queue: .main
        ) { _ in
            MainActor.assumeIsolated { rebuild() }
        }
    }

    /// The physical shape of the keyboard used last. On ISO keyboards key
    /// code 50 sits between the left Shift and Z and key code 10 is left of 1;
    /// on ANSI (and, for this purpose, JIS) keyboards key code 50 is left of 1.
    enum PhysicalShape: Sendable { case ansi, iso }

    static var physicalShape: PhysicalShape {
        KBGetLayoutType(Int16(LMGetKbdType())) == PhysicalKeyboardLayoutType(kKeyboardISO) ? .iso : .ansi
    }

    /// The key code that types `character` (lowercase) without modifiers in
    /// the active layout, if any.
    nonisolated static func keyCode(for character: String) -> Int64? {
        current.keyCodesByCharacter[character.lowercased()]
    }

    private static func rebuild() {
        var newNames: [Int64: String] = [:]
        var newCodes: [String: Int64] = [:]
        for keyCode in KeyNames.layoutDependentKeyCodes.sorted() {
            guard let text = translate(keyCode), !text.isEmpty else { continue }
            newNames[keyCode] = text.uppercased()
            if newCodes[text.lowercased()] == nil { newCodes[text.lowercased()] = keyCode }
        }
        let rebuilt = Tables(names: newNames, keyCodesByCharacter: newCodes)
        lock.lock()
        tables = rebuilt
        lock.unlock()
    }

    /// The character a key types with no modifiers, via UCKeyTranslate.
    private static func translate(_ keyCode: Int64) -> String? {
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue()
                ?? TISCopyCurrentASCIICapableKeyboardLayoutInputSource()?.takeRetainedValue(),
              let pointer = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else {
            return nil
        }
        let layoutData = Unmanaged<CFData>.fromOpaque(pointer).takeUnretainedValue() as Data
        var deadKeyState: UInt32 = 0
        var length = 0
        var characters = [UniChar](repeating: 0, count: 4)
        let status = layoutData.withUnsafeBytes { buffer -> OSStatus in
            guard let layout = buffer.baseAddress?.assumingMemoryBound(to: UCKeyboardLayout.self) else { return -1 }
            return UCKeyTranslate(
                layout,
                UInt16(keyCode),
                UInt16(kUCKeyActionDisplay),
                0,
                UInt32(LMGetKbdType()),
                // The mask (1 << bit), not the bit number: dead keys such as
                // ^ on AZERTY then report their own character.
                OptionBits(kUCKeyTranslateNoDeadKeysMask),
                &deadKeyState,
                characters.count,
                &length,
                &characters
            )
        }
        guard status == noErr, length > 0 else { return nil }
        let text = String(utf16CodeUnits: characters, count: length)
        // Control characters are not useful names.
        guard text.unicodeScalars.allSatisfy({ $0.value >= 0x20 }) else { return nil }
        return text
    }
}
