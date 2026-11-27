# Changelog

All notable changes to Lumerist are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Import keys** for culling in Lightroom's Import window: on the new
  Import page, pick an Include, an Exclude and a Toggle key. They act only
  while the Import window is in front and press Lightroom's own keys there
  (P, X and its Toggle key). "Move to the next photo" selects the next photo
  after Include or Exclude. No key is assigned until you pick one, and in the
  Import window the mouse wheel and the trackpad scroll Lightroom as usual.
  Lumerist recognises the window by the type and size of Lightroom's
  windows, never their text.
- **Lightroom key warnings**: a Command Key, Nudge Key, Import key or system
  key on one of Lightroom's own keys gets a note on its row, e.g. "Lightroom
  uses Z for “Toggle Zoom view” in Library. Lumerist takes it over there."
  It never stops you from saving.
- Tooltips explain Dial Keys, Nudge Keys, steps and the other kinds of keys.
  They follow the chosen theme and appear after one second.
- Japanese (JIS) keyboards: the drawn keyboard has their shape, and ¥, _,
  英数 and かな have names.

### Changed
- The keys Lumerist presses in Lightroom (shortcuts for commands, Import
  keys, Paste Settings from Previous) are found by the character they type
  on your keyboard layout, ⌘ and Ctrl shortcuts included. They stay right on
  Dvorak, AZERTY, QWERTZ and other layouts, and a key is never pressed just
  because of where it sits.
- Tab + Q is now the command "Paste settings from previous", listed under
  Edit. A Tab + Q left as it came from an earlier version becomes the
  command by itself.
- No default Palette Key on Japanese (JIS) keyboards, where the key left of 1
  switches the input method, nor where that key types a Latin letter or a
  digit (Hungarian). General says why, and any free key can be picked.
- Windows: AltGr is a modifier of its own, as Option is on the Mac, so
  AltGr + a key types its character and never sets off a Lumerist key. The
  input-method keys of Japanese and Korean keyboards can't be recorded.
- The × that clears a key turns red under the pointer.

### Fixed
- A keymap Lumerist can't read, such as one saved by a newer version, is no
  longer replaced by the standard layout: a copy is kept and the problem is
  logged. On the Mac, General → Profile shows the copy.

### Security
- **The plug-in obeys only the Lumerist app.** Every request from the app now
  carries a secret, the bridge token, that only your own account on the
  computer can read. The plug-in refuses anything without it and disconnects
  the sender. Before, another account on the same Mac or PC, or possibly a web
  page, could send Lightroom the commands Lumerist uses (reset, ratings,
  flags, presets). This is bridge protocol 4: the app and the plug-in update
  together, and the app installs the new plug-in by itself while Lightroom is
  closed. Thanks to the person who reported it privately.
- The plug-in's log keeps only a short, cleaned-up copy of what a sender
  wrote, so nobody can flood it or forge lines in it.
- Mac: a profile with an impossible key code no longer crashes the app when
  that key is used.
- Mac: a program pretending to be the plug-in can no longer keep the app busy
  by sending it empty lines.
- Releases: the release script downloads the Windows packages itself, from
  the CI run of the push to `main` for the released commit, so packages built
  by a pull request are never published. It also refuses to publish while the
  plug-in or font folders hold files git ignores.

## [0.9.1] — the first release with the repository public

Everything Lumerist does is described in the
[0.9.0 release notes](https://github.com/ziago/Lumerist/releases/tag/v0.9.0).

### Added
- **Report a problem…** in the menu bar (Mac) and notification area (Windows)
  menu, and in General → About & Support: it opens GitHub's bug form in the
  browser with the Lumerist version, the system, the kind of computer and the
  keyboard layout already filled in. Lumerist itself sends nothing.

### Changed
- The first-run guide and the General screen name the mouse and the trackpad
  together, on the Mac and on Windows: Dial Keys follow a two-finger swipe as
  well as the mouse wheel.
- Mac: the Profile row no longer says that old backups are converted when
  imported (import never did that); it now reads like the Windows one.
- The README shows Lumerist at work: animations of Dial Keys and the Palette,
  pictures of the setup; the bug report form asks about Windows too.
- The code was tidied before going public (unused pieces removed, repeated
  helpers merged), on the Mac, on Windows and in the plug-in alike. Nothing
  else changes in how Lumerist works.

## [0.9.0] — first public pre-release

### Added
- **Dial Keys**: hold a key and turn the mouse wheel, swipe on the trackpad,
  drag, or press ←/→ to move a Develop slider; ⇧ for the larger step; a quick
  tap still reaches Lightroom.
- **Standard layout** built on Capture One's default Speed Edit keys and
  translated to Lightroom Classic; Tab is the Combo Key, the key left of 1
  opens the Palette. Four combos are preset in Develop: Tab + A Auto Tone,
  Tab + W Auto White Balance, Tab + R Reset all, Tab + Q Paste Settings from
  Previous.
- **Nudge Keys**, **Command Keys** (Lightroom commands, Develop presets,
  Lightroom shortcuts, macros) and **combos**: hold the Combo Key and press
  another key, written Tab + K. Any Command Key or Nudge Key can be a combo;
  you record one by holding Tab while you press the key.
- **Masks follow**: with a mask selected, keys move the mask's own sliders;
  keys without a mask equivalent never touch the whole photo.
- **Library**: Quick Develop adjustments on every selected photo.
- **Palette** with favourite presets and Lightroom's *Create New Mask* tools
  (⇧-click adds, ⌥-click subtracts, ⇧⌥-click intersects with a mask
  selected); **Key Reference** panel, shown with Tab + H.
- **Value HUD** in Ruler and Compact styles with a labelled scale (every EV
  for exposure), on Lightroom's screen or under the pointer, placed by
  dragging it.
- Mouse and trackpad clicks: double-click (or middle-click) resets, the mouse
  side buttons go to the previous/next photo.
- About 130 Develop parameters, the standard layout, a step and a larger ⇧
  step for every slider (Quick Develop uses Lightroom's own button steps), a
  Keyboard view of every module, export/import of the layout.
- First-run guide that installs the Lightroom Classic plug-in and asks for the
  Accessibility permission.
- *File → Plug-in Extras → Open Lumerist* in Lightroom opens the app
  window (and starts the app if needed).
- Lumerist follows Lightroom Classic: the plug-in starts it in the background
  when Lightroom opens, and it quits 3 s after Lightroom quits (both optional).
- **Updates:** Lumerist checks GitHub's list of releases when it starts, at
  most once a day (on unless you untick it in the first-run guide or in
  *General*; the request carries nothing about you), and announces a new
  version in the menu bar or notification area with a link to its download
  page. Installing a new version keeps your keys and settings, and Lumerist
  updates its Lightroom plug-in by itself the next time Lightroom is closed.
- Universal app (Apple Silicon + Intel) for macOS 14 or later; Lightroom
  Classic 12 or later.
- **Windows 10 and 11** (x64 and ARM64): the same app, keys and plug-in, with
  Ctrl and Alt in place of ⌘ and ⌥, a per-user installer that needs no
  administrator rights, and a portable zip. Lumerist lives in the notification
  area; Ctrl+Esc pauses it. Mac and Windows layouts are kept separately.
  The Windows version is **not code-signed yet**: the first time, Windows
  shows *Windows protected your PC*; click **More info → Run anyway**. The
  README's FAQ explains why and how to check the SHA-256 published with every
  file. Code signing for Windows will come in a later release.
- Neon synthwave theme.
- Optional **Stay in touch**: a short question on the main window, at most
  three times and never while Lightroom is in front (also in *General → About
  & Support*), offering news about new versions and about other photography
  tools from the developer. Nothing is sent until you type your email and
  press **Keep me posted**; **Don't ask me again** stops it. Lumerist keeps
  only the date, never the email. Links to Galerist in *About & Support* and
  in the menu bar window open in the browser and carry no personal data.

Lumerist is made by the creator of [Galerist](https://galerist.photo/c/lk-release),
online galleries where photographers and videographers deliver their work to
clients.

[Unreleased]: https://github.com/ziago/Lumerist/compare/v0.9.1...HEAD
[0.9.1]: https://github.com/ziago/Lumerist/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/ziago/Lumerist/releases/tag/v0.9.0
