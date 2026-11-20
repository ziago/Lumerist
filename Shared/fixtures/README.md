# Shared test cases

Lumerist has two native apps: Swift on macOS (`Sources/`) and C# on Windows
(`Windows/`). Each platform keeps its own file formats and conventions, so a
profile saved on one system does not load on the other. What must behave the
same is pinned down here: both test suites read these files and must pass
every case.

| Path | What it pins down | Swift runner | C# runner |
| --- | --- | --- | --- |
| `gestures/*.json` | `KeyGestureEngine`: what every key, wheel and mouse event does | `Tests/LumeristCoreTests/SharedFixtureTests.swift` | `Windows/Lumerist.Core.Tests/GestureFixtureTests.cs` |
| `keymap-rules/*.json` | keymap validation and conflict reports | same | `Windows/Lumerist.Core.Tests/KeymapFixtureTests.cs` |
| `protocol/*.json` | bridge protocol 3: request lines, plug-in messages | same | `Windows/Lumerist.Core.Tests/ProtocolFixtureTests.cs` |
| `standard-layout.json` | the standard layout and default options | same | `Windows/Lumerist.Core.Tests/KeymapFixtureTests.cs` |
| `../contract.json` | ports, plug-in id, protocol version, consent hash | `Scripts/check-parity.py` | `Scripts/check-parity.py` |

A new feature or fix is finished when it has a case here and both runners
pass it. `python3 Scripts/check-parity.py` checks that every case ran on both
sides and that the shared constants match.

## Key names

Cases name keys by their position on the keyboard, never by a key code. Each
runner translates them with its own table: macOS key codes in Swift, Windows
scan codes in C#. The tables live in the test code only.

| Name | Key |
| --- | --- |
| `A` … `Z`, `0` … `9` | letter and digit keys (US positions) |
| `LeftOf1` | the key left of 1 (the Palette Key) |
| `Tab`, `Esc`, `Space`, `Return`, `Backspace` | as printed |
| `[`, `]` | the two keys right of P |
| `ArrowLeft`, `ArrowRight` | arrow keys |

A chord joins modifiers and a key with `+`: `Shift+Q`, `Combo+K`.

| Modifier | macOS | Windows |
| --- | --- | --- |
| `Shift` | ⇧ | Shift |
| `Alt` | ⌥ | Alt |
| `Primary` | ⌘ (Lightroom's command key) | Ctrl |
| `Control` | ⌃ | Ctrl |
| `Combo` | the Combo Key is held: the chord is a combo ("Tab + K") | same |

`Primary` and `Control` are both Ctrl on Windows, so a case never uses the two
in one chord.

## Gesture cases

```json
{ "id": "dial-keys/tap-is-replayed",
  "about": "A Dial Key tapped without a gesture reaches Lightroom in order.",
  "keymap": { },
  "steps": [
    { "down": "Q", "decision": "consume" },
    { "up": "Q", "decision": ["Q↓", "Q↑"], "state": { "engaged": false } }
  ] }
```

`keymap` lists changes to the standard layout (see below). Each step does one
thing and states what must follow:

| Step | Options | Engine call |
| --- | --- | --- |
| `"down": chord` | `repeat`, `at` (seconds), `in`, `textFocused` | key down |
| `"up": key` | `at`, `in` | key up |
| `"wheel": lines` | `modifiers`, `in` | notched wheel (point delta = lines × 10) |
| `"swipe": points` | `momentum`, `in` | trackpad scroll |
| `"click": button` | `count`, `in` | mouse down (`left`, `right`, `middle`, `back`, `forward`) |
| `"release": button` | `in` | mouse up |
| `"move": dx` | `modifiers`, `in` | pointer moved by `dx` points |
| `"paletteDidClose": true` | | the app closed the Palette |
| `"setKeymap": changes` | | load the standard layout plus `changes` |

`in` is the Lightroom context: `develop` (default), `masking` (Develop with
the Masking tool), `library`, `map` (any other module) or `away` (Lightroom
is not frontmost).

Expectations, all optional:

- `decision`: `"pass"`, `"consume"`, or the list of events posted instead.
  A key event is written as its key name plus `↓` or `↑`.
- `intents`: the exact list of intents. `lastIntent`: only the last one.
- `focusQueries`: how many times the engine asked whether a text field has
  focus during this step.
- `state`, checked after the step: `engaged`, `paused`, `paletteOpen`,
  `needsScrollEvents`, `activeParameter`, `heldEvents`.

Intents: `{"adjust": "develop/Exposure", "delta": 0.05}`, `{"reset": p}`,
`{"gestureBegan": p}`, `{"gestureEnded": p}`, `{"revealPanel": p}`,
`{"unavailableInMask": p}`, `{"action": action}`, `{"togglePause": true}`,
`"openPalette"`, `"closePalette"`, `"toggleKeyReference"`,
`"restorePointer"`. Deltas are compared to within 1e-9.

Actions: `{"command": "undo"}` (optional `"argument"`), `{"shortcut": chord}`,
`{"preset": uuid, "name": name}`, `{"macro": [{"action": action, "delay": s}]}`.

## Keymap changes

```json
{ "input": { "invertWheel": true },
  "palette": { "keepVisible": true },
  "systemKeys": { "comboKey": "H", "paletteKey": null },
  "slots": { "develop/Exposure": { "dial": null, "nudgeUp": "Combo+]", "normalStep": 3 } },
  "commandKeys": [ { "scope": "develop", "chord": "Combo+K", "title": "Undo", "action": { "command": "undo" } } ],
  "replaceCommandKeys": false }
```

Slot fields are merged into the existing slot; `null` clears one. Command
Keys are appended unless `replaceCommandKeys` is true.

## Keymap rule cases

`validate` is `"ok"` or the expected error: `{"error": "invalidStep",
"parameter": p}`, `{"error": "invalidOption", "option": name}`,
`{"error": "invalidCommandKey", "reason": text}`, `{"error": "invalidCombo",
"misuse": "dialKey" | "lightroomShortcut" | "systemKey", ...}` or
`{"error": "conflicts"}`.

`conflicts` gives the number of `errors` and `warnings` and a list of
`matches`: each must match at least one reported conflict by `severity`,
`chord`, `scope` (`null` for system keys), `usesSystem` (a system key role)
and `message`. Messages are compared only for chords without modifiers,
whose names read the same on both systems.

## Protocol cases

`requests.json`: a request, the id the client gives it, and the line it must
become (compared as JSON, ending with one `\n`), or the validation `error`.
`messages.json`: a line from the plug-in and the message it decodes to,
`null` when this build ignores it, or the decoding `error`.
