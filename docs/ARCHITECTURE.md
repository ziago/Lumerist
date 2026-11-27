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
| `Shared` | What both apps and the plug-in must agree on: `parameters.json`, `contract.json`, `update-public-keys.json` and the test cases in `fixtures/`. |
| `Scripts` | The catalog generator, the parity check, and the build and release scripts. |

## Keeping the two apps alike

The Mac and Windows apps are separate native programs: they share no code,
and each keeps its own file formats. What must behave the same lives in
`Shared/`:

- `parameters.json` is the parameter catalog (see below).
- `contract.json` holds the ports, the protocol version, the plug-in id and
  the consent version and hash.
- `update-public-keys.json` lists the public keys whose signature on a
  release's `SHA256SUMS` the updater trusts (`release.sh` signs with the
  matching private key, which stays in the maker's Keychain).
- `fixtures/` holds cases for the gesture engine, the keymap rules, the
  protocol and the standard layout. Both test suites run every case
  ([fixtures/README.md](../Shared/fixtures/README.md)).

`python3 Scripts/check-parity.py` fails when a side did not run every case,
when a side or the plug-in lacks a value of `contract.json`, when a side's
trusted update keys differ from `update-public-keys.json`, when a row of
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
  US position. Lightroom takes its shortcuts from its interface language,
  which the plug-in reports (protocol 4): `LightroomShortcuts.Language`
  holds each language Lightroom ships, from `Shared/lightroom-shortcuts.json`
  (`languages`): its name, its Import Toggle character (< in Italian and
  Swedish) and the keys its help lists differently, which the warnings use.
  The keys Lumerist presses change only for an override marked `press`
  (none today). The app installs the language in
  `LightroomShortcuts.activeLanguage`; unknown means English.
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
- **Protocol 4.** `LightroomRequest`, `LightroomMessage` and
  `LightroomCommandClient` (two TCP connections, each reconnecting on its own
  with a capped backoff). Every request carries the bridge token
  (`BridgeToken`), a secret only the user's account can read, without which
  the plug-in refuses it. The wire format is specified in
  [protocol-v4.md](protocol-v4.md).
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
- **Updates** (shared rules, `Shared/fixtures/updates`). `UpdateFeedRelease`
  reads GitHub's releases API; `UpdateChoice` takes the newest non-draft
  release with a `v<semver>` tag, only when strictly newer than the running
  version, and the files a system needs, by exact name from the API's
  `assets`; `UpdateURLRule` says which addresses a download and a release
  page may use; `UpdateChecksums` and `SignedChecksumList` check the signed
  `SHA256SUMS`, the per-file `.sha256` and the API digest (`UpdateTrust`
  holds the keys); `UpdateHighlights` turns the release notes' Highlights
  into plain text; `UpdatePromptSchedule` decides when the update window
  appears. `MacUpdate` holds the Mac's own steps: the private folder, the
  Developer ID and notarization checks, the disk image and the swap.

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
- `UpdateChecker`: the update check (see [Network](#network)). A newer
  version shows in the menu bar item, whose line opens its release page, and
  in the update window.
- `UpdatePrompter`, `UpdatePromptModel`, `UpdateWindow`, `UpdatePromptView`
  (`UpdatePrompt*.swift`): the update window in the chosen theme: "Lumerist
  X is out", "You have Y.", the release's Highlights as plain text, **Update
  now**, **Release page**, **Later** and a small *Skip this version*. It
  appears after the check at launch unless that version was skipped or
  *Later* (24 hours) has not run out, and always after *Check for updates*.
  Lumerist comes to the front with it, and it floats above Lightroom until
  answered. *Release page* and closing it count as *Later*. In the new
  version (started with `--updated`) it says the update is installed and,
  while Lightroom still runs the old plug-in, offers *Quit Lightroom &
  update*.
- `MacUpdateInstaller` and `UpdateDownload`: **Update now** (see
  [Network](#network)).
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
  events, which also carry Lightroom's interface language; presets and an unsupported-parameter check are available as queries.

## Network

The app and the plug-in talk only over `127.0.0.1`. Nothing about photos,
catalogs or keystrokes leaves the computer, and there is no telemetry. Each
app makes only three kinds of outbound request:

| When | Request | Code |
| --- | --- | --- |
| Lumerist starts and its last check is a day old or more (on by default), or the user presses *Check for updates* | `GET https://api.github.com/repos/ziago/Lumerist/releases?per_page=20` | Mac: `UpdateChecker`. Windows: `UpdateCheck` and `UpdateFeedClient` (Core), `UpdateChecker` (app) |
| The user types an email in *Stay in touch* and presses **Keep me posted** | `POST https://galerist.photo/api/lumenkeys/subscribe` | `GaleristSubscriptionClient` |
| The user presses **Update now** in the update window | `GET` of that release's files on GitHub: `SHA256SUMS`, `SHA256SUMS.sig`, the system's installer and its `.sha256` (`https://github.com/ziago/Lumerist/releases/download/<tag>/<name>`, redirected to GitHub's file hosts) | Mac: `MacUpdateInstaller`, `UpdateDownload`. Windows: `UpdatePreparation` (Core), `UpdateInstaller` (app) |

The update check:

- runs at launch at most once a day, unless the user unticks *Tell me when a
  new version is out* in the first-run guide or the automatic check in
  *General*; *Check for updates* always works by hand;
- remembers the time only after a real answer from GitHub, so a failure
  (offline, rate-limited) does not delay the next check;
- carries no identifier: it is a plain `GET` with `User-Agent: Lumerist`;
- opens a release page only when it is an https page of this project's
  releases on github.com, whatever the answer says;
- shows the release notes' Highlights as plain text only (no link, Markdown
  or HTML is rendered): at most 5 lines of at most 140 characters, 600 in
  all.

The update download, only after **Update now**:

- takes the file names and URLs only from the API's `assets` (`name` and
  `browser_download_url`, matched by exact name), never from release text;
  a URL must be exactly the release's download address;
- checks every step, the first URL and each redirect, against the hosts
  `api.github.com`, `github.com`, `objects.githubusercontent.com` and
  `release-assets.githubusercontent.com` (https, default port, no user
  info); at most 5 redirects;
- keeps no cookies, cache or credentials, sends only `User-Agent: Lumerist`,
  times out after 30 s of silence or 30 minutes in all, and stops at once
  past the size the API declares (at most 300 MiB for an installer);
- writes into a new folder only the user can read (mode 700, files 0600)
  in the user's temporary folder, deleted afterwards;
- installs nothing unless, in this order: `SHA256SUMS.sig` verifies under a
  key of `Shared/update-public-keys.json` (ECDSA P-256 over the exact bytes
  of `SHA256SUMS`); `SHA256SUMS` is well formed and lists the installer; the
  installer's SHA-256 equals the listed one, and the `.sha256` and the API
  digest when present. A release without `SHA256SUMS` or its signature is
  offered with its release page only.

On the Mac, after those checks: the disk image must carry the Developer ID
signature of team `JRNUV73KXY`; it is mounted read-only and hidden
(`hdiutil attach -nobrowse -readonly -noautoopen` at a mount point in the
private folder) and nothing on it is run; its `Lumerist.app` must meet the
Developer ID requirement (identifier `io.github.ziago.lumerist`, team
`JRNUV73KXY`; `SecStaticCodeCheckValidity`, every architecture and nested
code, strict) and be exactly the release's version, for this Mac's macOS and
processor. It is copied next to the running app with `ditto`, and the copy is
checked again (requirement, `spctl --assess --type execute` accepting it as
*Notarized Developer ID*, version). Then the copy and the running app swap
places (`renamex_np(RENAME_SWAP)`), the new app opens and, once it has
finished launching, the old copy is deleted and the old app quits; if it
does not start, the old copy is swapped back. Same Developer ID and
identifier, so macOS keeps the Accessibility permission. Lumerist never asks
for administrator rights: where it can't replace itself (a folder this
account may not change, a disk image, a translocated copy) the window says
so and offers the release page. Every failure leaves the running app as it
was and offers the release page.

On Windows, after those checks: the installer must start with `MZ`, and it
is hashed again, while it is held open against writes, just before it runs.
Only a copy of Lumerist installed by its installer (`%LOCALAPPDATA%\Programs\Lumerist`,
or the uninstall entry's `InstallLocation`) updates itself; a portable copy
is offered the release page. The installer, which installs for the current
user only and never asks for administrator rights, is started by its full
path, without a shell and without the `LUMERIST_*` variables, with
`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS` plus
`/LUMERISTRELAUNCH` and a `/LOG=` file (`update-installer.log` in
`%LOCALAPPDATA%\Lumerist\Logs`). Lumerist then releases its single-instance
lock and quits; the installer waits up to 30 s for it, replaces the files and
reopens it with `--after-update`, which shows that the update is installed
or, if it did not finish, offers the release page. An installer that asks
for administrator rights fails instead of prompting. Because the Windows
installer is not code-signed, the signed `SHA256SUMS` is what stops a
release published by anyone else from being installed.

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
| `LUMERIST_UPDATE_PRETEND_VERSION` | both apps, debug builds only (Windows: the Debug configuration) | The version Lumerist believes it is for the update check and the update window: with `0.9.0`, GitHub offers the real v0.9.1. A release build ignores it. |
| `LUMERIST_UPDATE_FEED_URL` | both apps, debug builds only (Windows: the Debug configuration) | A `file://` URL (or absolute path) of a JSON in the releases API's format, read instead of `api.github.com`; its assets may then also be `file://` URLs without a host. https downloads keep the host allowlist, and every other check stays. Any other value refuses the check. A release build ignores it. |
| `LUMERIST_UPDATE_E2E_DMG`, `LUMERIST_UPDATE_E2E_VERSION` | Swift tests | Enable the live test `liveDiskImageChecks` on a real release disk image (no download): every Mac check of **Update now** up to the swap, on a copy in a private folder. |
| `LUMERIST_RENDER_SETUP` | Mac app, development | Renders the setup guide, the menu bar window and the *General* sections (with *Stay in touch* before and after a sign-up) to PNG files in that folder, plus, under `themes/<look>/`, every main-window screen, the *Stay in touch* sheet (editing, error, sending, success, opened from About) and the update window (`update-*`: offered, not installable, not writable, downloading, installing, failed, installed) in each theme, then quits: no window, event tap, Lightroom connection, network request or saved setting. |
| `LUMERIST_RENDER_HUD` | Mac app, development | Renders the Value HUD styles, the floating panels and the settings that show them to PNG files in that folder (once per theme), then quits: no window, event tap or Lightroom connection. |
| `LUMERIST_PREVIEW_PANELS=1`, `LUMERIST_PREVIEW_OVERLAY=ruler\|compact` | Mac app, development | Opens the Palette preview and the Key Reference, or shows the Value HUD preview once, for visual checks. |

With either update override set, the check at launch runs at every launch
and its time is not saved. The app's debug builds are `swift build` /
`swift run`, or a bundle from `zsh Scripts/build-macos-app.sh --debug`
(signed like the others; it refuses `--release`).

On Windows, `Lumerist.exe --render-snapshots <folder>` renders every screen
to PNG files and quits; CI runs it on the packaged app.
