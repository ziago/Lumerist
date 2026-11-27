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
| `lightroom-keys/*.json` | the keys Lumerist presses in Lightroom for a keyboard layout and Lightroom's language: the Import keys and the fallback shortcuts | same | the C# runner of the same name |
| `protocol/*.json` | bridge protocol 4: request lines (with the bridge token), plug-in messages | same | `Windows/Lumerist.Core.Tests/ProtocolFixtureTests.cs` |
| `standard-layout.json` | the standard layout and default options | same | `Windows/Lumerist.Core.Tests/KeymapFixtureTests.cs` |
| `bug-report/*.json` | "Report a problem": the prefilled GitHub bug form | same | `Windows/Lumerist.Core.Tests/BugReportTests.cs` |
| `updates/*.json` | the updater: which release and files, the URL rules, the signed checksums, the Highlights, when the update window appears (see below) | `Tests/LumeristCoreTests/UpdateFixtureTests.swift` | planned |
| `../update-public-keys.json` | the public keys whose signature on `SHA256SUMS` is trusted | `UpdateFixtureTests.swift` and `Scripts/check-parity.py` | `Scripts/check-parity.py` |
| `../lightroom-shortcuts.json` | Lightroom Classic's own default keys in Library, Develop and the Import window, from Adobe's public help (see below) | `LightroomShortcutsTests.swift`; the warnings in `keymap-rules/lightroom-shortcuts.json` | the C# keymap rules read the same file |
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
| `LeftOf1` | the key left of 1 on a US (ANSI) keyboard, which types `: macOS key code 50, Windows scan code 0x29. The standard layout's Palette Key (the apps put it left of 1 on ISO keyboards too, and set none on JIS keyboards or where that key types a letter or digit), and the Toggle key Lightroom gets with the US layout the cases use (see below) |
| `Tab`, `Esc`, `Space`, `Return`, `Backspace` | as printed (`Backspace` is the Mac's Delete key) |
| `ForwardDelete`, `Home`, `End`, `PageUp`, `PageDown` | as printed (`ForwardDelete` is Delete on Windows, ⌦ on the Mac) |
| `[`, `]` | the two keys right of P |
| `=`, `-`, `'`, `;`, `\`, `,`, `.`, `/` | the other punctuation keys, at their US positions |
| `IsoLeftOfZ` | the ISO key between the left Shift and Z (lightroom-keys cases with `"keyboard": "iso"` only): Windows scan code 0x56, macOS key code 50, while `LeftOf1` is then 10 on the Mac |
| `ArrowLeft`, `ArrowRight`, `ArrowUp`, `ArrowDown` | arrow keys |
| `F1` … `F12` | function keys |

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

`keymap` lists changes to the standard layout (see below).
`lightroomImportKeys` (optional) changes the keys Lightroom gets from the
Import keys, which are otherwise those of the US layout: `include`, `exclude`,
`toggle` and `nextPhoto`, each a chord; `"toggle": null` is a layout with no
Toggle. Each step does one thing and states what must follow:

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
the Masking tool), `library`, `map` (any other module), `import` (Lightroom's
Import window is in front: the app recognises the window and uses the module
name `import`, which Lightroom itself never reports) or `away` (Lightroom is
not frontmost).

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

A command is one of Lightroom's (docs/protocol-v4.md), which the app sends
to the plug-in, or one Lumerist runs itself by pressing Lightroom's own
shortcut, which sends nothing to the plug-in. There is one of those:
`pasteSettingsFromPrevious`, Lightroom's Paste Settings from Previous (⌥⌘V
on macOS, Ctrl+Alt+V on Windows, with the key that types v on the active
layout), which the standard layout puts on `Combo+Q`. It takes no argument.
Each app reads a Command Key saved with the old default, the shortcut
`Primary+Alt+V` titled "Paste settings from previous", as this command.

## Keymap changes

```json
{ "input": { "invertWheel": true },
  "palette": { "keepVisible": true },
  "systemKeys": { "comboKey": "H", "paletteKey": null },
  "importKeys": { "include": "Z", "exclude": "X", "toggle": null, "advance": true },
  "slots": { "develop/Exposure": { "dial": null, "nudgeUp": "Combo+]", "normalStep": 3 } },
  "commandKeys": [ { "scope": "develop", "chord": "Combo+K", "title": "Undo", "action": { "command": "undo" } } ],
  "replaceCommandKeys": false }
```

Slot fields are merged into the existing slot; `null` clears one. Command
Keys are appended unless `replaceCommandKeys` is true. `importKeys` sets
the Import keys (`include`, `exclude`, `toggle`: a chord or `null`) and
`advance` ("Move to the next photo"); the standard layout assigns none.

In the Import window an Import key sends Lightroom's own key as
`{"shortcut": chord}`: Include `P`, Exclude `X`, Toggle `LeftOf1`, and
`ArrowRight` after Include or Exclude when `advance` is on. Those are the
keys of the US layout. The apps find them by the character they type,
never by position: the keys that type p and x, and for Toggle the key that
types, alone (no modifier, not a dead key), the character of Lightroom's
language (< in Italian and Swedish, ` otherwise), else a plain `, else a
plain <: `LeftOf1` on US keyboards, the key left of Z on Italian, German or
French ones. With none (Russian, Czech, U.S. International – PC…) there is
no Toggle: its key is taken and sends nothing (`lightroomImportKeys`). The
`lightroom-keys` cases pin that choice down.

## Lightroom keys cases

Which keys Lumerist presses in Lightroom, without the engine:

```json
{ "id": "lightroom-keys/italian-lightroom-on-an-italian-keyboard-toggles-with-less-than",
  "lightroomLanguage": "it",
  "keyboard": "iso",
  "layout": { "LeftOf1": "\\", "IsoLeftOfZ": "<", "\\": "ù" },
  "deadKeys": [],
  "importKeys": { "include": "P", "exclude": "X", "toggle": "IsoLeftOfZ" },
  "fallbacks": { "autoTone": "Primary+U", "pasteSettingsFromPrevious": "Alt+Primary+V" } }
```

- `lightroomLanguage`: Lightroom's interface language as the apps write it
  ("it", "pt_br"), or `null` (not known yet, which means English).
- `keyboard`: `"ansi"` (default) or `"iso"`.
- `layout`: what keys type alone, as changes to the US layout (letters,
  digits, `LeftOf1` types `, the punctuation keys type their US characters);
  `null` types nothing. `IsoLeftOfZ` types nothing unless given. No two
  keys may type the same character: the apps search keys in different
  orders. The ⌘ (Ctrl) table is the same as the plain one.
- `deadKeys`: keys that are dead keys, which type nothing alone.
- `importKeys`: the keys `include`, `exclude` and `toggle` press (a chord,
  or `null` for no Toggle).
- `fallbacks`: the chord pressed for `autoTone`, `undo`, `redo`, `flagPick`,
  `flagReject`, `flagRemove`, `showClipping` and
  `pasteSettingsFromPrevious`.

## Keymap rule cases

`validate` is `"ok"` or the expected error: `{"error": "invalidStep",
"parameter": p}`, `{"error": "invalidOption", "option": name}`,
`{"error": "invalidCommandKey", "reason": text}`, `{"error": "invalidCombo",
"misuse": "dialKey" | "lightroomShortcut" | "systemKey" | "importKey", ...}`
(`systemKey` and `importKey` name the `role`) or `{"error": "conflicts"}`.

`lightroomLanguage` (optional, a code or `null`) is Lightroom's interface
language for the warnings about its keys; absent or `null` is English.

`conflicts` gives the number of `errors` and `warnings` and a list of
`matches`: each must match at least one reported conflict by `severity`,
`chord`, `scope` (`null` for system keys and Import keys, except a warning
about one of Lightroom's keys, which names its module), `usesSystem` (a
system key role), `lightroomAction` (see below) and `message`. Messages are compared only for chords without modifiers,
whose names read the same on both systems.

## Lightroom's own keys

`Shared/lightroom-shortcuts.json` lists Lightroom Classic's default keys for
Library, Develop and the Import window, per system, in the key names above,
with the source and retrieval date of each section (the Import window's keys
are community reports, marked `communityReported`). A tap key on one of them
in its own place gets a warning, which never blocks saving:

- Command Keys and Nudge Keys in their module (a Command Key for all modules
  in each), and Import keys in the Import window. Never a Dial Key (a tap
  still reaches Lightroom) or a combo (Lumerist's own key).
- System keys where they take their key: the Palette Key in Develop, a plain
  Key Reference toggle and the pause toggle in Library and Develop, one
  warning per module with that module's `scope`. Never the Combo Key: tapped
  alone it still reaches Lightroom, like a Dial Key.
- A Nudge Key with no modifiers also takes its Shift chord (the larger
  step): when the key itself is free, the Shift chord is checked.
- An Import key never warns about the key it sends itself (`importRole`).
- Letters and digits match by the character the key types on the active
  layout, arrows, F-keys and named keys by key. Punctuation (`LeftOf1` and
  the keys above) never matches.
- The first entry in file order names the key: the warning reads
  "Lightroom uses Z for “Toggle Zoom view” in Library. Lumerist takes it
  over there." (`in Develop`, `in the Import window`), and its
  `lightroomAction` is that name. It replaces the general warning about
  typing keys and modifiers for the same chord.
- Lightroom takes its keys from its interface language. `languages` has one
  entry per language Lightroom Classic ships, with its name, its
  `LrLocalization` codes, its Import Toggle character and the keys its
  help page lists differently (`overrides`, each with its evidence). A
  code picks a language exactly, then by the part before `_` (never for zh),
  else English. Overrides replace an entry's keys in that language for the
  warnings; for the keys Lumerist presses only when `press` is true and the
  entry has `fallbackFor` (none today). `Scripts/generate-lightroom-shortcuts.py`
  refuses an override that lands on another action's key in the same place.

## Protocol cases

`requests.json`: a request, the id the client gives it, and the line it must
become (compared as JSON, ending with one `\n`), or the validation `error`.
`messages.json`: a line from the plug-in and the message it decodes to,
`null` when this build ignores it, or the decoding `error`.

## Update cases

`updates/` is one group; the second part of each id says the kind of case
(`updates/<kind>/…`). Regular expressions below are anchored with `\A` and
`\z`: `$` would also match before a final line break.

**`releases`**: `current` (the running version), `releases` (GitHub's
releases API: `tag_name`, `draft`, `prerelease`, `html_url`, `body`,
`assets` with `name`, `browser_download_url`, `size`, `digest`, `state`),
`allowFileURLs` (optional, the debug test feed), and `expect`: `null` (no
update) or `{version, tag, page, platforms}`, where each of `mac`,
`windows-x64` and `windows-arm64` is `null` (not installable there) or
`{installer, checksum, sums, signature}`, each `{name, url, size, sha256}`
or `null` (only `checksum` may be).

- A tag is `v` + `MAJOR.MINOR.PATCH` with an optional `-prerelease`
  (ASCII letters, digits and `-`, dot-separated), no leading zeros, no
  build metadata, at most 64 characters; `current` is the same without `v`
  (no update when it isn't one).
- Drafts are skipped; the newest release wins (the first in the API's
  order on a tie), only when strictly newer than `current`: never a
  downgrade. `page` is `html_url` when it is a trusted release page, else
  `https://github.com/ziago/Lumerist/releases`.
- The installer is `Lumerist-<v>.dmg` or `Lumerist-<v>-windows-<x64|arm64>-setup.exe`,
  its checksum the same name + `.sha256`, then `SHA256SUMS` and
  `SHA256SUMS.sig`; each found by exact name among the assets whose `state`
  is absent or `uploaded`. A name listed twice, a size of 0 or over the
  limit (installer 314572800, `.sha256` 1024, `SHA256SUMS` 65536, `.sig`
  1024), or a `browser_download_url` other than, as text,
  `https://github.com/ziago/Lumerist/releases/download/<tag>/<name>` (with
  `allowFileURLs`, also a `file:` URL without a host) makes the file
  invalid. A `digest` of `sha256:<64 hex>` gives `sha256` in lowercase;
  none or another algorithm gives `null`; a malformed `sha256:` makes the
  file invalid.
- A system is `null` when its installer, `SHA256SUMS` or the signature is
  missing or invalid, or its `.sha256` is present but invalid.

**`urls`**: `url`, `allowFileURLs` (optional), `tag` and `name` (for
`asset`), and `expect` with any of:

- `hop`: a download step may go there: `https`, a host (any case) among
  `api.github.com`, `github.com`, `objects.githubusercontent.com`,
  `release-assets.githubusercontent.com`, written without `%`, port absent
  or 443, no user info; or, with `allowFileURLs`, `file:` without a host;
- `asset`: the `browser_download_url` rule above;
- `releasePage`: `https` to `github.com`, port absent or 443, no user info,
  a path without `%` or a `..` segment, under `/ziago/Lumerist/releases/`.

**`checksums`**, by the case id's last part:

- `sha256-file-…`: `checksumFile` (the per-file `.sha256`), `name`,
  `expect` `{sha256}` or `{error}` (`malformedChecksumFile`,
  `nameMismatch`). One line, with or without a final LF or CRLF:
  `<64 hex>`, two spaces or a space and `*`, then the file's name.
- `list-…`: `sums` (`SHA256SUMS`; its UTF-8 bytes are what is signed),
  `signature` (`SHA256SUMS.sig`), `keys` (SPKI DER in base64), `name`,
  `expect` `{sha256}` or `{error}` (`badSignature`, `malformedList`,
  `notListed`). The signature comes first: whitespace around it is
  trimmed; the rest must be base64 (`[A-Za-z0-9+/]+={0,2}`, a length that
  is a multiple of 4, nothing inside) of a DER ECDSA P-256 / SHA-256
  signature that verifies under any key of `keys` (unreadable keys are
  skipped). Then the list: UTF-8, no CR; split on LF, a single final LF
  allowed; every line `<64 lowercase hex>␠␠<name>`, a name made of ASCII
  letters, digits, `.`, `_`, `+`, `-`, not `.` or `..`, never twice; the
  order is not checked.
- `agree-…`: `actual`, `listed`, `checksumFile`, `digest` (`null` when
  the release has none), `expect` `"ok"` or `{"error": "mismatch",
  "source": "list" | "checksumFile" | "digest"}`, checked in that order,
  any case.

The `list-` cases are signed with two throwaway test keys made only for
these files (their private halves are not in the repository); one case checks that
Lumerist's real key does not verify them.

**`highlights`**: `body` (release notes) and `expect` (the lines).

- The section starts after the first line that reads `### Highlights`
  (spaces around and case ignored) and ends at the next line starting with
  `#` after leading spaces or tabs. Lines end with LF or CRLF.
- A line starting with `- ` or `* ` (after leading spaces or tabs) starts a
  bullet; a following line indented by a space or tab continues it, joined
  with one space; a blank line or unindented plain text ends it.
- Plain text: U+0000–U+001F, U+007F–U+009F, U+2028 and U+2029 become
  spaces; U+061C, U+200E, U+200F, U+202A–U+202E, U+2066–U+2069 and U+FEFF
  are removed. Links `!?\[([^\]]*)\]\([^)]*\)` become `$1`. Then code spans
  `` `([^`]*)` `` keep their content as is, and outside them these become
  `$1`, in this order: `\*\*(?=\S)(.+?)(?<=\S)\*\*`,
  `(?<![A-Za-z0-9])__(?=\S)(.+?)(?<=\S)__(?![A-Za-z0-9])`,
  `\*(?=\S)(.+?)(?<=\S)\*`, `(?<![A-Za-z0-9])_(?=\S)(.+?)(?<=\S)_(?![A-Za-z0-9])`.
  Runs of whitespace become one space; empty bullets are skipped.
- Lengths count Unicode scalars. Over 140: keep 139, back to the last
  space when the next scalar isn't one and there is a space, trim, add
  `…`. At most 5 bullets and 600 scalars: the bullet that would pass 600
  and those after it are left out. With none, the window shows "See what's
  new on the release page."

**`prompt`**: `available`, `current`, `skippedVersion`, `laterUntil`
(ISO 8601 or `null`), `now`, `trigger` (`launch` or `manual`), `expect`
(shown or not). Both versions must be `<semver>` and `available` newer;
`manual` always shows; otherwise not for the skipped version, and not while
`now` is before `laterUntil` when that is at most 24 hours ahead. Action
cases: `{"action": "later", "now"}` sets `laterUntil` to `now` + 24 hours;
`{"action": "skip", "version"}` sets `skippedVersion`.
