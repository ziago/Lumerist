# Architecture

Lumerist has two halves that talk over the loopback interface:

```
 keyboard / wheel / mouse
          │
          ▼
┌──────────────────────────┐   JSON lines, 127.0.0.1   ┌─────────────────────────────┐
│ Lumerist.app (Swift)    │ ── 50550: requests ──────▶ │ Lumerist.lrplugin (Lua)    │
│ event tap → gesture      │                            │ one worker task →           │
│ engine → intents         │ ◀── 50551: acks, events ── │ LrDevelopController, …      │
└──────────────────────────┘                            └─────────────────────────────┘
```

## Core (`Sources/LumeristCore`)

- **Parameter catalog.** `Shared/parameters.json` is the single source of
  truth for every adjustable parameter (id, label, section, scope, unit,
  steps, limits). `python3 Scripts/generate-parameters.py` generates
  `ParameterCatalogData.swift` and the plug-in's `Parameters.lua`;
  `--check` runs in the tests so the two never drift apart.
- **Keymap.** Per parameter and scope (Develop, Library): a Dial Key (hold),
  Nudge Keys (tap −/+), custom step sizes. Plus Command Keys (Lightroom
  command, Develop preset, Lightroom shortcut, macro), System Keys (Combo Key,
  Palette Key, Key Reference toggle, pause toggle), Palette options and input
  options. `Keymap.standardLayout` is the default.
- **Combos.** A combo is a `KeyChord` with the pseudo-modifier
  `KeyModifiers.combo` (not a macOS modifier; key events never carry it): it
  works only while the Combo Key is held and reads "Tab + K"
  (`KeyChord.displayText(comboKey:)`). Command Keys and each Nudge Key can be
  combos; Dial Keys, system keys and Lightroom shortcuts can't (validation).
  Conflicts compare plain chord equality, so "K" and "Tab + K" never clash.
  Keymaps stay format 3: older files with `requiresLayerKey`,
  `nudgeRequiresLayerKey` or a `layerKey` system key are read and converted to
  combos, and only the new form is written.
- **Gesture engine.** `KeyGestureEngine` is a pure state machine: it receives
  key, scroll and mouse events plus the Lightroom context (module, tool) and
  decides whether to pass, swallow, or replay each event, emitting
  `KeyIntent`s (adjust, reset, gesture begin/end, command, Palette, Key
  Reference, pause…). It holds a key back until it knows whether it is a tap
  (replayed to Lightroom, in order) or a gesture. While the Combo Key is held,
  the next key is looked up as a combo in the same indexes; the Combo Key
  tapped alone is replayed. With the Masking tool active it remaps sliders to
  their `local_*` equivalents.
- **Protocol 3.** `LightroomRequest` / `LightroomMessage` and
  `LightroomCommandClient` (two TCP connections with per-channel reconnection
  and backoff). The wire format is specified in
  [protocol-v3.md](protocol-v3.md).
- **Stay in touch sign-up** (`GaleristSubscription.swift`). `GaleristOptIn`
  holds the exact consent texts of version `lk-optin-v2` (and, for the drift
  guard only, those of `lk-optin-v1`), the local email check, the two-letter language, the base URL and the tracked Galerist links
  (`TrackedLink`, `https://galerist.photo/c/lk-*`). `GaleristSubscription` is
  the JSON body (optional fields left out when absent),
  `GaleristSubscriptionOutcome` maps the server's answer to the message shown
  in the app (only a 2xx with `{"ok":true}` counts as subscribed), and
  `GaleristSubscriptionClient` sends the single request. Its transport is
  injectable, so the unit tests never touch the network; one live test runs
  only when `LUMERIST_E2E_BASE_URL` is set (see below).

## App (`Sources/LumeristApp`)

- `CommandStore` — app state: bridge client, Lightroom context, history.
- `KeyEngineMonitor` — a `CGEventTap` on keys, wheel and mouse that feeds the
  engine; replayed taps are inserted with `tapPostEvent` so typing order is
  preserved. Wheel direction follows the physical movement, whatever macOS
  natural scrolling does.
- `IntentDispatcher` — turns intents into requests at the chosen update rate
  (adjustments to the same slider are summed), runs macros, sends Lightroom's
  own shortcuts, and falls back to a shortcut when the SDK refuses a command.
- `KeymapStore` — persistence of the keymap.
- `UpdateChecker` — the update check: asks GitHub for the list of releases
  when the user presses *Check for updates*, or once a week if the user turned
  that on (off by default). It never downloads anything; a newer version only
  opens its release page.
- `StayInTouchPrompter` / `StayInTouchModel` / `StayInTouchSheet`
  (`StayInTouchView.swift`) — the optional "Stay in touch" sheet on the main
  window: one email field, "Not now", "Keep me posted", "Don't ask me again".
  `StayInTouchSchedule` (Core, pure) decides when it may appear: never after a
  sign-up or "Don't ask me again", at most 3 times (first opportunity, then
  ≥ 3 days, then ≥ 14 days later); the prompter shows it at most once per
  launch and only while the main window is visible and key and the app is
  frontmost, after a 1.2 s pause (never over Lightroom, never on a window that
  only flashed at a hidden launch). General → About & Support opens it on
  request. The schedule lives in the user defaults
  (`org.lumerist.stay-in-touch.*` and `lumerist.galerist.subscribed-at`);
  the email is never stored.
- `SupportLinks` — every external link the app shows (GitHub, donations,
  Galerist). Links open in the default browser.
- UI in the "Synth Grid" style (`Design/`, `Views/`, `Panels/`): the
  `ValueHUD`, the Palette and the Key Reference are non-activating panels so
  Lightroom keeps keyboard focus.

## Plug-in (`Lightroom/Lumerist.lrplugin`)

- `LrForceInitPlugin = true`, so the bridge starts with Lightroom instead of on
  first use.
- One worker task applies requests in order and merges consecutive changes to
  the same parameter. `LrDevelopController.setValue` is asynchronous, so the
  worker remembers its own recent targets to avoid losing steps to stale
  reads. Every SDK call runs inside `LrTasks.pcall`.
- A plug-in reload creates a new Lua environment while old tasks may keep
  running; the bridge therefore stores an ownership "generation" in plug-in
  preferences so stale loops stop by themselves.
- The "send" socket is recreated rather than reconnected.
- Module and tool are polled about every 0.15 s and pushed as `context`
  events; presets and an unsupported-parameter check are available as queries.

## Network

The app and the plug-in talk only over `127.0.0.1`. Nothing about photos,
catalogs or keystrokes leaves the Mac, and there is no telemetry. The app
makes only two kinds of outbound request, and the user starts both:

| When | Request | Code |
| --- | --- | --- |
| The user presses *Check for updates*, or the weekly check they turned on is due (checked at launch) | `GET https://api.github.com/repos/ziago/Lumerist/releases?per_page=20` | `UpdateChecker` |
| The user types an email in *Stay in touch* and presses **Keep me posted** | `POST https://galerist.photo/api/lumenkeys/subscribe` | `GaleristSubscriptionClient` |

The sign-up request:

- is sent only from **Keep me posted** (or Return in the field), after a
  local check: with an implausible email nothing is sent;
- carries a JSON body with `email`, `lumenkeysUpdates` and `galeristNews`
  (both always `true` in `lk-optin-v2`, whose text covers both),
  `consentVersion` and, when present, `language` (two lowercase letters from
  the first of the Mac's preferred languages), `appVersion` and `prompt`
  (`1`–`3`, or `"about"` when opened from About & Support); the header
  `User-Agent: Lumerist/<version>`;
- uses an ephemeral `URLSession` (no cookies, cache or stored credentials) and
  a 15-second timeout;
- leaves only the date on the Mac (`lumerist.galerist.subscribed-at`), never
  the email.

The consent texts are stored by Galerist as proof of consent, so a test pins
their SHA-256 (the same check runs on the Galerist side); changing a text
needs a new consent version.

Links (GitHub, donations, the Galerist links `https://galerist.photo/c/lk-*`)
are opened in the browser with `NSWorkspace`; the app itself sends nothing for
them. The `lk-*` code only tells Galerist where the link was.

### Environment overrides

| Variable | Used by | Effect |
| --- | --- | --- |
| `LUMERIST_GALERIST_BASE_URL` | app | Replaces `https://galerist.photo` for the sign-up request (for tests against a local server). Must be `https` to `galerist.photo`, or `http` to `127.0.0.1`, with no user, password, query or fragment. Any other value makes the app refuse to send rather than fall back to production. Empty means production. The tracked links always point at production. |
| `LUMERIST_E2E_BASE_URL` | tests only | Enables the live test `liveServerAcceptsASignUpAndRejectsABadDomain` against that server (same rules as above). It sends two requests: one sign-up, and one with a `.invalid` domain that must be refused. |
| `LUMERIST_E2E_EMAIL` | tests only | The address the live test signs up (default `lumerist-e2e+<timestamp>@galerist.photo`). |
| `LUMERIST_RENDER_SETUP` | app, development | Renders the setup guide, the menu bar window and the *General* sections (with *Stay in touch* before and after a sign-up) to PNG files in that folder, plus, under `themes/<look>/`, every main-window screen and the *Stay in touch* sheet (editing, error, sending, success, opened from About) in each theme, then quits: no window, event tap, Lightroom connection, network request or saved setting. |
