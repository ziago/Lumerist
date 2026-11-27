# Architecture

Lumerist has two halves that talk over the loopback interface: an app (one
for the Mac, one for Windows) and a Lightroom Classic plug-in.

```
 keyboard / wheel / mouse
          │
          ▼
 ┌───────────────────────────┐                           ┌────────────────────────────┐
 │ Lumerist app              │   JSON lines, 127.0.0.1   │ Lumerist.lrplugin (Lua)    │
 │ Mac: Swift, Windows: C#   │ ── 50550: requests ─────▶ │ inside Lightroom Classic:  │
 │ input hook → gesture      │                           │ one worker task →          │
 │ engine → intents          │ ◀── 50551: acks, events ─ │ LrDevelopController, …     │
 └───────────────────────────┘                           └────────────────────────────┘
```

## Repository

| Path | What it is |
| --- | --- |
| `Sources/LumeristCore` | The Mac app's logic, with no UI: catalog, keymap, gesture engine, protocol, plug-in install, the "Stay in touch" rules. Tested by `Tests/LumeristCoreTests`. |
| `Sources/LumeristApp` | The macOS app (SwiftUI and AppKit). |
| `Windows/Lumerist.Core` | The same logic in C# (.NET), plus the parts of the Windows screens that can be tested without a window. Tested by `Windows/Lumerist.Core.Tests` (xUnit). |
| `Windows/Lumerist.App` | The Windows app (WPF). |
| `Windows/Installer` | The Windows installer (Inno Setup). |
| `Lightroom/Lumerist.lrplugin` | The plug-in (Lua). Tested by `Tests/Lua` with LuaJIT (`Scripts/test-lua.sh`). |
| `Shared` | What both apps and the plug-in must agree on: `parameters.json`, `contract.json` and the test cases in `fixtures/`. |
| `Scripts` | The catalog generator, the parity check, and the build and release scripts. |

## Keeping the two apps alike

The Mac and Windows apps are separate native programs: they share no code,
and each keeps its own file formats. What must behave the same lives in
`Shared/`:

- `parameters.json` is the parameter catalog (see below).
- `contract.json` holds the ports, the protocol version, the plug-in id and
  the consent version and hash.
- `fixtures/` holds cases for the gesture engine, the keymap rules, the
  protocol and the standard layout. Both test suites run every case
  ([fixtures/README.md](../Shared/fixtures/README.md)).

`python3 Scripts/check-parity.py` fails when a side did not run every case,
when a side or the plug-in lacks a value of `contract.json`, when a row of
[feature-spec.md](feature-spec.md) has no macOS or Windows status, or when a
theme colour differs between the apps. CI runs it after each test suite.

## Core (`Sources/LumeristCore`, `Windows/Lumerist.Core`)

The two cores have the same parts, under the same names where the languages
allow.

- **Parameter catalog.** `Shared/parameters.json` is the single source of
  truth for every adjustable parameter (id, label, section, scope, unit,
  steps, limits). `python3 Scripts/generate-parameters.py` writes three files
  from it: the plug-in's `Parameters.lua`, the Mac's
  `ParameterCatalogData.swift` and the Windows `ParameterCatalogData.g.cs`.
  With `--check` it fails when one of them is out of date; the Lua tests and
  CI run that check.
- **Keymap.** Per parameter and scope (Develop, Library): a Dial Key (hold),
  Nudge Keys (tap −/+) and custom step sizes. Plus Command Keys (Lightroom
  command, Develop preset, Lightroom shortcut, macro), System Keys (Combo Key,
  Palette Key, Key Reference toggle, pause toggle), Palette options and input
  options, and the Import keys for Lightroom's Import window (none assigned
  in the standard layout). `Keymap.standardLayout` is the default.
- **Lightroom's own keys.** `Shared/lightroom-shortcuts.json` lists
  Lightroom Classic's default shortcuts for Library, Develop and the Import
  window, taken from Adobe's public help (the Import window's from community
  reports). `python3 Scripts/generate-lightroom-shortcuts.py` checks it and
  writes the Mac's `LightroomShortcutData.swift`; the Windows core reads the
  JSON. A tap key or a system key on one of them gets a warning in the
  conflict report (`LightroomShortcuts`), which never blocks saving.
- **Pressing Lightroom's keys.** Lightroom finds its shortcuts by the
  character a key types, so Lumerist presses them by character, never by
  position. `LayoutCharacters` is what the active layout types, alone and
  with ⌘ (the ⌘ table differs on "Dvorak – QWERTY ⌘"). `FallbackShortcuts`
  holds the shortcuts for commands the SDK can't run, and `ImportShortcuts`
  the Import window's (Toggle: the key that types a plain `, else <, else
  none). `AppCommand` is a command Lumerist runs itself by pressing a
  shortcut, with no request to the plug-in (`pasteSettingsFromPrevious`,
  ⌥⌘V). A letter no key types comes from the ASCII-capable layout, then the
  US position.
- **Combos.** A combo is a `KeyChord` with the pseudo-modifier
  `KeyModifiers.combo` (not a real modifier; key events never carry it). It
  works only while the Combo Key is held and reads "Tab + K"
  (`KeyChord.displayText(comboKey:)`). Command Keys and each Nudge Key can be
  combos; Dial Keys, system keys and Lightroom shortcuts can't (validation).
  Conflicts compare plain chord equality, so "K" and "Tab + K" never clash.
  Keymaps stay format 3: older files with `requiresLayerKey`,
  `nudgeRequiresLayerKey` or a `layerKey` system key are read and converted to
  combos, and only the new form is written.
- **Gesture engine.** `KeyGestureEngine` is a pure state machine. It receives
  key, scroll and mouse events plus the Lightroom context (module, tool) and
  decides whether to pass, swallow or replay each event. It emits
  `KeyIntent`s: adjust, reset, gesture begin/end, command, Palette, Key
  Reference, pause… It holds a key back until it knows whether it is a tap
  (replayed to Lightroom, in order) or a gesture. While the Combo Key is held,
  the next key is looked up as a combo; the Combo Key tapped alone is
  replayed. With the Masking tool active, sliders map to their `local_*`
  equivalents.
- **Protocol 3.** `LightroomRequest`, `LightroomMessage` and
  `LightroomCommandClient` (two TCP connections, each reconnecting on its own
  with a capped backoff). The wire format is specified in
  [protocol-v3.md](protocol-v3.md).
- **Plug-in install.** `PluginInstallOperation` installs, updates and removes
  the plug-in with plain file moves. A replaced copy goes to a backup folder
  outside Lightroom's Modules folder (`~/Library/Application
  Support/Lumerist/plugin-backups`, `%APPDATA%\Lumerist\plugin-backups`):
  Lightroom scans Modules, and two copies of one plug-in disable each other.
  The apps replace an older copy by themselves while Lightroom is closed.
- **Stay in touch sign-up** (`GaleristSubscription`). `GaleristOptIn` holds
  the exact consent texts of version `lk-optin-v2` (and, for the drift guard
  only, those of `lk-optin-v1`), the local email check, the two-letter
  language, the base URL and the tracked Galerist links (`TrackedLink`,
  `https://galerist.photo/c/lk-*`). `GaleristSubscription` is the JSON body
  (optional fields left out when absent). `GaleristSubscriptionOutcome` maps
  the server's answer to the message shown in the app; only a 2xx with
  `{"ok":true}` counts as subscribed. `GaleristSubscriptionClient` sends the
  single request. Its transport is injectable, so the unit tests never touch
  the network; one live test runs only when `LUMERIST_E2E_BASE_URL` is set
  (see below). `StayInTouchSchedule` decides when the prompt may appear:
  never after a sign-up or "Don't ask me again", at most 3 times (first
  opportunity, then ≥ 3 days, then ≥ 14 days later).

## Mac app (`Sources/LumeristApp`)

- `CommandStore`: app state, the bridge client, the Lightroom context and the
  history.
- `KeyEngineMonitor`: a `CGEventTap` on keys, wheel and mouse that feeds the
  engine. Replayed taps are inserted with `tapPostEvent`, so typing order is
  kept. Wheel direction follows the physical movement, whatever macOS
  natural scrolling does.
- `ImportWindowDetector`: while Lightroom is frontmost, every 0.25 s, reads
  the role, subrole, size and "has a title" of Lightroom's windows through
  Accessibility (never their text) and tells `KeyEngineMonitor` when the
  Import window is in front (`ImportWindowRule` in the core decides). The
  engine then sees the module `import`, where only the Import keys act.
- `IntentDispatcher`: turns intents into requests at the chosen update rate
  (adjustments to the same slider are summed), runs macros, sends Lightroom's
  own shortcuts, runs `AppCommand`s, and falls back to a shortcut when the
  SDK refuses a command.
- `KeyboardLayout`: reads the active input source and the ASCII-capable one
  through UCKeyTranslate into `LayoutCharacters` (key names and the keys to
  press), again whenever the input source changes, when the Import keys are
  looked up again too. The keyboard's shape (ANSI, ISO, JIS) comes from
  `LMGetKbdType`; it picks the Palette Key of the standard layout (none on
  JIS) at the first run and on "Restore standard layout", never later, since
  it follows the last keyboard typed on.
- `KeymapStore`: saves and loads the keymap. A stored keymap it can't read
  (`KeymapLoad`) is copied to `~/Library/Application
  Support/Lumerist/keymap-backups` and never written over; the standard
  layout is used until the user changes a key.
- `PluginInstaller`: installs, updates and removes the plug-in that ships
  inside the app; files are replaced only while Lightroom is closed.
- `UpdateChecker`: the update check (see [Network](#network)). It never
  downloads anything; a newer version only opens its release page.
- `StayInTouchPrompter`, `StayInTouchModel`, `StayInTouchSheet`
  (`StayInTouchView.swift`): the optional "Stay in touch" sheet on the main
  window: one email field, "Not now", "Keep me posted", "Don't ask me again".
  The prompter shows it at most once per launch, after a 1.2 s pause, and only
  while the main window is visible and key and the app is frontmost: never
  over Lightroom, never on a window that only flashed at a hidden launch.
  General → About & Support opens it on request. The schedule lives in the
  user defaults (`org.lumerist.stay-in-touch.*` and
  `lumerist.galerist.subscribed-at`); the email is never stored.
- `SupportLinks`: every external link the app shows (GitHub, donations,
  Galerist). Links open in the default browser.
- `Design/`, `Views/`, `Panels/`: the themes (Galerist light and dark, Miami)
  and the screens. The `ValueHUD`, the Palette and the Key Reference are
  non-activating panels, so Lightroom keeps keyboard focus.

## Windows app (`Windows/Lumerist.App`)

The same pieces as on the Mac, with Windows means:

- `AppController` connects the bridge, the input hook, the intent dispatcher,
  the keymap and the plug-in install (the Mac's `CommandStore`).
- `Input/InputHook` uses low-level keyboard and mouse hooks to feed the same
  engine; `Input/InputSender` replays held keys with `SendInput`, marked so
  the hook lets them through.
- `Input/ImportWindowDetector`: the input hook asks it on each event. It
  reads the style and size of Lightroom's foreground window and the class and
  size of its owner (never their text), and `ImportWindowRule` in the core
  decides whether the Import window is in front.
- `TrayIcon` is the notification-area icon (the Mac's menu bar item).
- `Services/PluginInstaller` installs the plug-in into
  `%APPDATA%\Adobe\Lightroom\Modules`.
- Keymap and settings live in `%APPDATA%\Lumerist` (`keymap.json`,
  `settings.json`).

## Plug-in (`Lightroom/Lumerist.lrplugin`)

- `LrForceInitPlugin = true`, so the bridge starts with Lightroom instead of on
  first use.
- One worker task applies requests in order and merges consecutive changes to
  the same parameter. `LrDevelopController.setValue` is asynchronous, so the
  worker remembers its own recent targets to avoid losing steps to stale
  reads. Every SDK call runs inside `LrTasks.pcall`.
- A plug-in reload creates a new Lua environment while old tasks may keep
  running. The bridge therefore stores an ownership "generation" in plug-in
  preferences, so stale loops stop by themselves.
- The "send" socket is recreated rather than reconnected.
- Module and tool are polled about every 0.15 s and pushed as `context`
  events; presets and an unsupported-parameter check are available as queries.

## Network

The app and the plug-in talk only over `127.0.0.1`. Nothing about photos,
catalogs or keystrokes leaves the computer, and there is no telemetry. Each
app makes only two kinds of outbound request:

| When | Request | Code |
| --- | --- | --- |
| Lumerist starts and its last check is a day old or more (on by default), or the user presses *Check for updates* | `GET https://api.github.com/repos/ziago/Lumerist/releases?per_page=20` | Mac: `UpdateChecker`. Windows: `UpdateCheck` and `UpdateFeedClient` (Core), `UpdateChecker` (app) |
| The user types an email in *Stay in touch* and presses **Keep me posted** | `POST https://galerist.photo/api/lumenkeys/subscribe` | `GaleristSubscriptionClient` |

The update check:

- runs at launch at most once a day, unless the user unticks *Tell me when a
  new version is out* in the first-run guide or the automatic check in
  *General*; *Check for updates* always works by hand;
- remembers the time only after a real answer from GitHub, so a failure
  (offline, rate-limited) does not delay the next check;
- carries no identifier: it is a plain `GET` with `User-Agent: Lumerist`;
- opens a release page only when it is an https page of this project's
  releases on github.com, whatever the answer says.

The sign-up request:

- is sent only from **Keep me posted** (or Return in the field), after a
  local check: with an implausible email nothing is sent;
- carries a JSON body with `email`, `lumenkeysUpdates` and `galeristNews`
  (both always `true` in `lk-optin-v2`, whose text covers both),
  `consentVersion` and, when present, `language` (two lowercase letters from
  the first preferred language), `appVersion` and `prompt` (`1`–`3`, or
  `"about"` when opened from About & Support); the header
  `User-Agent: Lumerist/<version>`;
- keeps no cookies, cache or stored credentials, follows no redirect, and
  times out after 15 seconds;
- leaves only the date on the computer (`lumerist.galerist.subscribed-at` in
  the Mac's user defaults, `settings.json` on Windows), never the email.

The consent texts are stored by Galerist as proof of consent, so a test pins
their SHA-256 (the same check runs on the Galerist side); changing a text
needs a new consent version.

Links (GitHub, donations, the Galerist links `https://galerist.photo/c/lk-*`)
open in the browser; the app itself sends nothing for them. The `lk-*` code
only tells Galerist where the link was.

### Environment overrides

| Variable | Used by | Effect |
| --- | --- | --- |
| `LUMERIST_GALERIST_BASE_URL` | both apps | Replaces `https://galerist.photo` for the sign-up request (for tests against a local server). Must be `https` to `galerist.photo`, or `http` to `127.0.0.1`, with no user, password, query or fragment. Any other value makes the app refuse to send rather than fall back to production. Empty means production. The tracked links always point at production. |
| `LUMERIST_E2E_BASE_URL` | Swift tests | Enables the live test `liveServerAcceptsASignUpAndRejectsABadDomain` against that server (same rules as above). It sends two requests: one sign-up, and one with a `.invalid` domain that must be refused. |
| `LUMERIST_E2E_EMAIL` | Swift tests | The address the live test signs up (default `lumerist-e2e+<timestamp>@galerist.photo`). |
| `LUMERIST_MODULES_DIR`, `LUMERIST_BACKUP_DIR`, `LUMERIST_PLUGIN_SOURCE` | both apps, debug builds only | Replace Lightroom's Modules folder, the plug-in backup folder and the bundled plug-in, for testing the install without touching Lightroom. A release build ignores them. |
| `LUMERIST_RENDER_SETUP` | Mac app, development | Renders the setup guide, the menu bar window and the *General* sections (with *Stay in touch* before and after a sign-up) to PNG files in that folder, plus, under `themes/<look>/`, every main-window screen and the *Stay in touch* sheet (editing, error, sending, success, opened from About) in each theme, then quits: no window, event tap, Lightroom connection, network request or saved setting. |
| `LUMERIST_RENDER_HUD` | Mac app, development | Renders the Value HUD styles, the floating panels and the settings that show them to PNG files in that folder (once per theme), then quits: no window, event tap or Lightroom connection. |
| `LUMERIST_PREVIEW_PANELS=1`, `LUMERIST_PREVIEW_OVERLAY=ruler\|compact` | Mac app, development | Opens the Palette preview and the Key Reference, or shows the Value HUD preview once, for visual checks. |

On Windows, `Lumerist.exe --render-snapshots <folder>` renders every screen
to PNG files and quits; CI runs it on the packaged app.
