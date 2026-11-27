import AppKit
import Carbon.HIToolbox
import Foundation
import LumeristCore

/// Reads the active keyboard layout, so key names match what is printed on
/// the user's keys (AZERTY, QWERTZ, Italian…), and so Lightroom's own
/// shortcuts are pressed by the character they type (⌘Z, P, the Import
/// Toggle): `LayoutCharacters` in the core. Tables are rebuilt on the main
/// thread whenever the input source changes; `didChange` is posted then.
@MainActor
enum KeyboardLayout {
    /// Immutable snapshots swapped under a lock, so a layout change on the
    /// main thread never races a reader on another thread.
    private struct Tables: Sendable {
        /// The active layout, or the ASCII-capable one when the active
        /// input source has no key data.
        var characters: LayoutCharacters?
        /// The ASCII-capable layout macOS keeps for shortcuts (the active
        /// one when it is ASCII-capable).
        var asciiCapable: LayoutCharacters?
        var names: [Int64: String] = [:]
    }
    private nonisolated static let lock = NSLock()
    private nonisolated(unsafe) static var tables = Tables()
    private static var observer: NSObjectProtocol?

    /// Posted on the main thread after the tables were rebuilt for another
    /// input source.
    static let didChange = Notification.Name("org.lumerist.keyboard-layout-did-change")

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
            MainActor.assumeIsolated {
                rebuild()
                NotificationCenter.default.post(name: didChange, object: nil)
            }
        }
    }

    /// What the active layout types; nil only when no layout has key data.
    nonisolated static var characters: LayoutCharacters? { current.characters }

    /// The ASCII-capable layout's keys, for letters the active layout lacks.
    nonisolated static var asciiCapableCharacters: LayoutCharacters? { current.asciiCapable }

    /// The physical shape of the keyboard used last (`LMGetKbdType`, which
    /// follows the last keyboard typed on and the Keyboard Setup Assistant).
    static var physicalShape: KeyboardShape {
        switch KBGetLayoutType(Int16(LMGetKbdType())) {
        case PhysicalKeyboardLayoutType(kKeyboardISO): .iso
        case PhysicalKeyboardLayoutType(kKeyboardJIS): .jis
        default: .ansi
        }
    }

    private static func rebuild() {
        let asciiSource = TISCopyCurrentASCIICapableKeyboardLayoutInputSource()?.takeRetainedValue()
        let asciiCapable = asciiSource.flatMap(characters(of:))
        // A layout without key data (some older third-party ones): the
        // ASCII-capable layout stands in for names and shortcuts.
        let activeSource = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue()
        let active = activeSource.flatMap(characters(of:)) ?? asciiCapable
        let rebuilt = Tables(characters: active, asciiCapable: asciiCapable, names: active?.names ?? [:])
        lock.lock()
        tables = rebuilt
        lock.unlock()
    }

    /// The active layout's name as macOS shows it ("Italian – Pro", "ABC"), or nil.
    static var currentName: String? {
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let pointer = TISGetInputSourceProperty(source, kTISPropertyLocalizedName) else {
            return nil
        }
        return Unmanaged<CFString>.fromOpaque(pointer).takeUnretainedValue() as String
    }

    /// What the keys of one input source type, through UCKeyTranslate; nil
    /// when the source has no key data.
    private static func characters(of source: TISInputSource) -> LayoutCharacters? {
        guard let pointer = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        let layoutData = Unmanaged<CFData>.fromOpaque(pointer).takeUnretainedValue() as Data
        let keyboardType = UInt32(LMGetKbdType())
        let commandState = UInt32(cmdKey >> 8) & 0xFF
        return layoutData.withUnsafeBytes { buffer -> LayoutCharacters? in
            guard let layout = buffer.baseAddress?.assumingMemoryBound(to: UCKeyboardLayout.self) else { return nil }
            var plain: [Int64: String] = [:]
            var command: [Int64: String] = [:]
            var display: [Int64: String] = [:]
            for keyCode in LayoutCharacters.searchOrder {
                plain[keyCode] = translate(layout, keyCode, modifierState: 0, keyboardType: keyboardType, display: false)
                command[keyCode] = translate(layout, keyCode, modifierState: commandState, keyboardType: keyboardType, display: false)
                display[keyCode] = translate(layout, keyCode, modifierState: 0, keyboardType: keyboardType, display: true)
            }
            return LayoutCharacters(plain: plain, command: command, display: display)
        }
    }

    /// The character a key types. A dead key types nothing by itself (nil),
    /// unless `display` asks for the accent it shows.
    private static func translate(_ layout: UnsafePointer<UCKeyboardLayout>, _ keyCode: Int64, modifierState: UInt32,
                                  keyboardType: UInt32, display: Bool) -> String? {
        var deadKeyState: UInt32 = 0
        var length = 0
        var characters = [UniChar](repeating: 0, count: 4)
        let status = UCKeyTranslate(
            layout,
            UInt16(keyCode),
            UInt16(display ? kUCKeyActionDisplay : kUCKeyActionDown),
            modifierState,
            keyboardType,
            // The mask (1 << bit), not the bit number: dead keys such as ^ on
            // AZERTY then report their own character.
            display ? OptionBits(kUCKeyTranslateNoDeadKeysMask) : 0,
            &deadKeyState,
            characters.count,
            &length,
            &characters
        )
        guard status == noErr, length > 0 else { return nil }
        let text = String(utf16CodeUnits: characters, count: length)
        // Control characters are neither names nor shortcuts.
        guard text.unicodeScalars.allSatisfy({ $0.value >= 0x20 && $0.value != 0x7F }) else { return nil }
        return text
    }
}
