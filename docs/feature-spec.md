# Lumerist — feature specification

Lumerist ships for macOS and Windows together, and every feature lands on both.
Each table has a column per system.

Status legend: **done** (implemented and covered by tests), **verify** (done
but still to be confirmed inside Lightroom Classic), **in progress**,
**planned**, **n/a** (the feature does not exist on that system; the reason is
given).

Keys are written as on a Mac. On Windows ⌘ is Ctrl, ⌥ is Alt and ⌃Esc is
Ctrl+Esc. Profiles use each system's own format and do not move between
systems.

## Keys

| Feature | Behaviour | macOS | Windows |
| --- | --- | --- | --- |
| Dial Keys | Hold + wheel/trackpad (notched lines 1:1, trackpad travel configurable, inertia ignored), hold + drag (optionally only with the left button down, sensitivity 1–30, pointer returns after release), hold + ←/→. ⇧ = larger step; "Large step by default" option; scroll direction option. A tap without a gesture reaches Lightroom in order. | done / verify drag | done / verify drag |
| Nudge Keys | Tap = one step down/up (⇧ larger step), autorepeat; each of the two can be a combo on its own (e.g. Nudge − Tab + [, Nudge + plain ]). | done / verify | verify |
| Command Keys | Tap = Lightroom command (undo/redo, auto tone/white balance, reset, next/previous photo, flags, rating, colour label, module, tool, reveal panel, zoom, clipping, masks, copy/paste settings), Develop preset (by UUID), one of Lightroom's own shortcuts, or a macro (ordered steps with delays). Commands the SDK cannot run in the current module fall back to Lightroom's own shortcut. | done / verify | verify |
| Combos | Hold the Combo Key and press another key: the combo, written "Tab + K", is a key of its own for a Command Key or a Nudge Key, while K alone keeps its job. Recorded by holding the Combo Key while recording a key. The Combo Key tapped alone, or followed by a key that is no combo, reaches Lightroom in order. Dial Keys, Lightroom shortcuts and the system keys except the Key Reference toggle can't be combos. In the standard layout (Develop): Tab + A Auto Tone, Tab + W Auto White Balance, Tab + R Reset all, Tab + Q Paste Settings from Previous (the command `pasteSettingsFromPrevious`, which Lumerist runs itself by pressing Lightroom's ⌥⌘V with the key that types v; keymaps with the old ⌥⌘V shortcut are read as it). | done / verify | verify |
| System Keys | Combo Key Tab (hold), Palette Key left of 1 (hold; none on JIS keyboards, which have no key there, or where that key types a letter or digit, such as "0" on Hungarian keyboards; General says why), Key Reference toggle Tab + H (a combo, Develop and Library), pause toggle ⌃Esc; each can be cleared (no Palette Key = no Palette). Set in General. | done | done (Ctrl+Esc, tried on Windows 11) |
| Per-module maps | Develop (Develop sliders) and Library (Quick Develop, relative, every selected photo). Other modules pass keys through. | done / verify Library | verify |
| Masks follow | With the Masking tool and a mask selected, Dial/Nudge Keys and reset act on `local_*` sliders; keys without a mask equivalent swallow scrolling and show a notice, taps still reach Lightroom. | done / verify names | verify |
| Mouse | Reset with double-click, middle-click, both or off; side buttons = previous/next photo. | done / verify | verify |
| Import keys | Culling in Lightroom's Import window, which Lumerist recognises itself (Lightroom reports the module as Library): Include, Exclude and Toggle send Lightroom's own P, X and Toggle key, by the character the key types, never by position: P and X are the keys that type p and x, and Toggle is the key that types a plain ` (no modifier, not a dead key; US and UK keyboards), else the key that types a plain < (Italian, German, French…). With neither (Russian, Czech, U.S. International – PC…) Toggle is off: its key sends nothing and the Import page says so. The keys are found again whenever the input source changes. None is assigned until the user picks keys. "Move to the next photo" (off by default) also sends → after Include or Exclude. There the Develop and Library keys, combos, the Palette and the Key Reference toggle pause, a gesture already under way ends, and the mouse wheel and trackpad scroll Lightroom as usual; text fields and the pause toggle work as everywhere. Import keys can't be combos, can't share a key or use the pause toggle. On the Mac the window is recognised through Accessibility by the type and size of Lightroom's windows, never their text; on Windows by the style and size of Lightroom's foreground window and its owner, never their text. | done / verify in Lightroom | done / verify in Lightroom |
| Lightroom key warnings | A Command Key, Nudge Key or Import key on one of Lightroom Classic's own default keys in its place (Develop, Library or the Import window), and a system key where it takes its key (the Palette Key in Develop, a plain Key Reference toggle and the pause toggle in Library and Develop), gets a warning on its row, e.g. "Lightroom uses Z for “Toggle Zoom view” in Library. Lumerist takes it over there." It never blocks saving. Dial Keys, the Combo Key and combos never warn; punctuation keys are not matched, since they move between keyboard layouts. The list comes from Adobe's public help (Import window: community reports) in `Shared/lightroom-shortcuts.json`. | done | done / verify in Lightroom |
| Conflicts | Detected and listed; a key can be both a Dial Key (hold) and a Command Key (tap); a combo and the plain key are different keys. | done | verify |

The behaviour in this table is pinned down by the shared cases in
`Shared/fixtures`, which both apps must pass.

## Screens and panels

| Feature | macOS | Windows |
| --- | --- | --- |
| Develop / Library pages: Sliders / Quick Develop (catalog sections, search, open/close all, Dial · Nudge − · Nudge + · Step per parameter, combos shown as "Tab + K"), Commands, Keyboard (drawn keyboard of the module, plain and combo layers, conflicts); Key Reference lists combos in their own group | done / verify | verify |
| Import page: Include, Exclude and Toggle key boxes (empty by default) with the Lightroom key each sends, "Move to the next photo", and what Lumerist reads to recognise the window | done / verify | done / verify |
| Settings: Mouse & Trackpad, Floating panels (Value HUD, Palette, Key Reference), General (startup, System Keys, Lightroom link and options, profile, diagnostics) | done / verify | verify |
| Accessibility permission in General | done | n/a (Windows needs no permission) |
| Palette panel (favourite presets, Create New Mask tools; with a mask selected ⇧ add, ⌥ subtract, ⇧⌥ intersect; pin, remembered position) | done / verify in Lightroom | verify |
| Key Reference panel (current module, search, pages, draggable) | done / verify | verify |
| Value HUD (Ruler, Compact; opacity; hide after N s or never; Lightroom's screen or under the pointer; placed by dragging) | done / verify | verify |
| Quick window: menu bar on macOS, notification area on Windows | done / verify | verify |
| Debug log, start at login, start hidden, quit with Lightroom (now or after a pause), smoothness (update rate), export/import/restore layout, parameter check | done | verify |
| A saved keymap this version can't read (written by a newer one, or damaged) is never written over: Lumerist keeps a copy (Mac: ~/Library/Application Support/Lumerist/keymap-backups), says so, and uses the standard layout until the user changes a key | done | done / verify |
| Stay in touch (optional, consent `lk-optin-v2`): a sheet on the main window with one email field, a one-line notice with the Privacy link, "Not now", "Keep me posted" and "Don't ask me again". Shown at most 3 times (at the first opportunity after the setup guide, then ≥ 3 days, then ≥ 14 days later), at most once per launch, only while the main window is key and the app frontmost (never over Lightroom); never after a sign-up or "Don't ask me again". General → About & Support opens it on request. One request, only on "Keep me posted"; afterwards only the date is kept, never the email | done / Galerist endpoint (v2) live before release | verify |
| Galerist credit and links: credit line and "Visit Galerist" in About & Support, "What is Galerist?" in the setup guide, a quiet item in the quick window (tracked `galerist.photo/c/lk-*` links, no personal data) | done | verify |

## Plug-in

One plug-in serves both systems; the few differences sit behind Lightroom's
`WIN_ENV` / `MAC_ENV` flags.

| Feature | macOS | Windows |
| --- | --- | --- |
| Protocol 3 over loopback (two `LrSocket`s), ids, acks with value/range/unit, context events (module/tool), presets and catalog queries | done | done (same plug-in) / verify |
| Single worker task, merged adjustments, stale-read chain, generations shared through plug-in prefs, forced init at startup, send-socket rebind | done | done (same plug-in) / verify |
| ~130 catalog parameters incl. hue wrap, choice stepping, Crop zoom, `local_*` | done / verify names in Lightroom | done (same plug-in) / verify |
| One history step per gesture (`startTracking`/`stopTracking`) | verify | verify |
| Open Lumerist (File > Plug-in Extras) and start it with Lightroom: `open` and the app's setting on macOS; on Windows the path and setting the app writes to `%APPDATA%\Lumerist\plugin-link.json` | done | plug-in done / app verify |

## Installation and distribution

| Feature | macOS | Windows |
| --- | --- | --- |
| Native app: Apple Silicon and Intel (macOS 14+); x64 and Arm64 (Windows 10 and 11) | in progress | verify |
| Download on GitHub Releases: signed and notarized DMG; installer for the current user (no administrator rights) and a portable zip, not code-signed in 0.9.0 (explained in the README and SECURITY.md) | in progress | in progress |
| First-run guide: install the plug-in, restart Lightroom, grant the permission (macOS only), try a Dial Key | in progress | verify |
| Plug-in bundled in the app; install, update and remove from the app; it updates itself whenever Lightroom is closed, and the Windows installer installs it | in progress | verify |
| Lightroom Classic 12+ (`LrSdkMinimumVersion = 12.0`) | in progress | verify |
| Key names shown in the current keyboard layout (capitals only where they keep the letter: ß, ı and a Turkish i stay as they are; a lone combining mark sits on ◌; the JIS keys ¥, _, 英数 and かな; the ASCII-capable layout for a layout without key data) | in progress | verify |
| Update check (GitHub releases): at launch, at most once a day, on unless unticked in the first-run guide or General; a new version is announced in the menu bar or notification area; manual check in General; installing a new version keeps keys and settings | in progress | verify |
| Report a problem: in the menu bar or notification area menu and in About & Support, opens GitHub's bug form in the browser with the version, system, computer and keyboard filled in; the app sends nothing | done | done (verify on a PC) |
| Homebrew cask | planned | n/a (Homebrew is for macOS) |

## Planned

For both systems:

- More themes, including calmer, more understated ones.
- Colour grading with the mouse (X = hue, Y = saturation, wheel = luminance) and a colour-wheel HUD.
- Crop panel and screen-zone keys (edges, corners, pan, rotate) beyond Crop zoom.
- Panel keys for Color & HSL.
- Preset source global vs catalog in the presets query (needs a protocol argument).
- UI translations.
