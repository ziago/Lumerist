# Changelog

All notable changes to Lumerist are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.9.1] — the first release with the repository public

Everything Lumerist does is described in the
[0.9.0 release notes](https://github.com/ziago/Lumerist/releases/tag/v0.9.0).

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
