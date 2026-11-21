# Contributing to Lumerist

Thanks for wanting to help! Ideas, bug reports, documentation, translations
and code are all welcome.

- **Questions and ideas** → [Discussions](https://github.com/ziago/Lumerist/discussions)
- **Bugs** → [open an issue](https://github.com/ziago/Lumerist/issues/new/choose)
  with your system (macOS or Windows version, Apple Silicon/Intel Mac or
  x64/Arm PC), Lightroom Classic version and language, keyboard layout, and
  the two log files (see the README FAQ).
- **Code** → a pull request against `main`. For larger changes, open a
  discussion first so we can agree on the approach.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Building from source

Lumerist is two native apps that share one Lightroom plug-in: Swift on macOS
(`Sources/`) and C# on Windows (`Windows/`). See [Both platforms](#both-platforms).

### macOS

Requirements: macOS 14 or later, Xcode 16 or later (Swift 6), Python 3, and
[LuaJIT](https://luajit.org) for the plug-in tests (`brew install luajit`).

```zsh
git clone https://github.com/ziago/Lumerist.git
cd Lumerist
swift test                                   # Swift tests
zsh Scripts/test-lua.sh                      # plug-in tests (LuaJIT, Lua 5.1 like Lightroom)
python3 Scripts/generate-parameters.py --check
zsh Scripts/build-macos-app.sh               # builds and signs build/Lumerist.app
zsh Scripts/install-local-plugin.sh --replace   # with Lightroom Classic closed
python3 Scripts/check-parity.py --side swift # shared cases and constants
```

- `build-macos-app.sh` signs with your first "Apple Development" identity when
  one exists, so macOS keeps the Accessibility permission across rebuilds. Set
  `LUMERIST_SIGN_IDENTITY=-` for an ad-hoc signature.
- `install-local-plugin.sh --replace` refuses to run while Lightroom Classic is
  open and keeps a backup of the previous copy outside Lightroom's Modules
  folder. Restart Lightroom after installing: it caches plug-in code.
- `swift Scripts/render-icon.swift` rebuilds the app icons in `Resources/` from
  the SVGs in `Resources/Icon`, and `swift Scripts/docs-render-banner.swift`
  redraws the README banner and the social preview in `docs/images`.

### Windows

Requirements: Windows 10 or 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download)
and Python 3. The core library and its tests also build on macOS and Linux with
the .NET SDK, so the shared cases can be checked from a Mac.

```powershell
dotnet test Windows                             # C# tests, including the shared cases
python Scripts/check-parity.py --side csharp
dotnet publish Windows/Lumerist.App -c Release -r win-x64 --self-contained true -o build/windows-x64
```

The last line makes a folder with `Lumerist.exe` and the plug-in next to it,
which runs on a PC without .NET installed (use `win-arm64` for ARM PCs). The
app builds on macOS too (`EnableWindowsTargeting`), but runs only on Windows.
`Scripts/render-windows-icon.py` redraws `Lumerist.ico` from the SVG icons, and
`Scripts/make-windows-fonts.py` makes the static font weights WPF needs from the
variable fonts in `Resources/Fonts`.

`Lumerist.exe --render-snapshots <folder>` writes a PNG of every screen in each
theme and quits, to compare the Windows windows with the Mac's.
`Lumerist.exe --install-plugin` installs or updates the Lightroom plug-in
without a window and exits with 0 (done), 1 (failed) or 2 (Lightroom Classic is
open); the installer runs it after copying the app.

Release packages are built on Windows, from a clean commit, with
[Inno Setup 6](https://jrsoftware.org/isinfo.php) installed:

```powershell
powershell -ExecutionPolicy Bypass -File Scripts\build-windows-installer.ps1
```

It writes the installers, the portable zips and their checksums for x64 and
arm64 to `build\release\<version>\windows`. `Scripts/release.sh` on the Mac
publishes them with the Mac app, and refuses to when one is missing or was
built from another commit.

## Project layout

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). In short:

| Path | What |
| --- | --- |
| `Sources/LumeristCore` | macOS: keymap model, gesture engine, protocol, client, parameter catalog |
| `Sources/LumeristApp` | macOS: SwiftUI app, event tap, panels, Value HUD |
| `Windows/Lumerist.Core` | Windows: the same core in C# |
| `Windows/Lumerist.App` | Windows: WPF app, keyboard and mouse hooks, panels, Value HUD |
| `Lightroom/Lumerist.lrplugin` | the Lua plug-in, one for both systems |
| `Shared/parameters.json` | single source of truth for parameters |
| `Shared/fixtures` | test cases both apps must pass ([format](Shared/fixtures/README.md)) |
| `Shared/contract.json` | ports, plug-in id and other values both apps and the plug-in share |
| `Tests/` | Swift tests and the LuaJIT harness with Lightroom stubs |
| `Scripts/` | build, release, test and code-generation scripts |
| `Resources/` | the Mac app's Info.plist and icons; the fonts both apps bundle |
| `docs/` | architecture, protocol, feature list |

### Adding a parameter

Edit `Shared/parameters.json`, then run `python3 Scripts/generate-parameters.py`
— it regenerates `Sources/LumeristCore/ParameterCatalogData.swift`,
`Windows/Lumerist.Core/ParameterCatalogData.g.cs` and
`Lightroom/Lumerist.lrplugin/Parameters.lua`. Never edit those by hand.

### Developer hooks

| Variable | Effect |
| --- | --- |
| `LUMERIST_RENDER_HUD=<folder>` | renders every Value HUD style, the floating panels and the keyboard map to PNG files (`-galerist` / `-miami`, plus light and dark where a theme has both) and quits (no window, no event tap, no Lightroom connection) |
| `LUMERIST_RENDER_SETUP=<folder>` | renders the setup guide, the General sections, the menu bar window and every screen in each theme to PNG files and quits |
| `LUMERIST_PREVIEW_OVERLAY=ruler\|compact` | shows a HUD preview at launch |
| `LUMERIST_PREVIEW_PANELS=1` | opens the Palette and Key Reference panels at launch |
| `LUMERIST_MODULES_DIR`, `LUMERIST_BACKUP_DIR` | stage the plug-in into another folder (tests); read by `install-local-plugin.sh` and by debug builds of both apps |
| `LUMERIST_PLUGIN_SOURCE` | the plug-in folder a debug build installs from |
| `LUMERIST_SIGN_IDENTITY`, `LUMERIST_BUILD_NUMBER` | code-signing identity and build number for `build-macos-app.sh` |

Run the renderers against the built app, e.g.
`LUMERIST_RENDER_HUD=/tmp/hud build/Lumerist.app/Contents/MacOS/LumeristApp`.
The overrides for the Stay in touch sign-up and its live test are listed in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#environment-overrides).

## Both platforms

Every feature and fix lands on macOS and Windows in the same pull request.
Each app keeps its own system's conventions (key codes and ⌘/⌥ on the Mac,
scan codes and Ctrl/Alt on Windows, each system's settings storage), so the
two apps share behaviour, not files.

- **Describe the behaviour once.** Add or change a case in `Shared/fixtures`
  (key names are logical, like `Q` or `Combo+K`), then make both the Swift and
  the C# tests pass it.
- **Implement it twice**, in `Sources/` and `Windows/`, following each
  system's conventions. The plug-in is shared: system differences go behind
  `WIN_ENV` / `MAC_ENV`, with a LuaJIT test for each branch.
- **Keep the core portable.** The gesture engine, keymap rules and protocol
  stay pure logic without UI or system calls, so they can be ported and tested
  on any machine.
- **Run `python3 Scripts/check-parity.py`.** It fails when a shared case runs
  on one side only, when ports, the plug-in id or the consent texts differ,
  when a theme colour differs, or when a row of the feature list has no
  status for one system.
- **If you can only do one side**, say so in the pull request: a maintainer
  opens an issue labelled `parity`, and no release ships while one is open.
- **Only what exists on one system** (the Mac's Accessibility permission and
  notarization, the Windows registry) may differ. Mark it **n/a** in
  [docs/feature-spec.md](docs/feature-spec.md) with the reason.

## Guidelines

- Keep the gesture engine pure and covered by tests; behaviour changes need a
  test.
- Every change to the plug-in needs a LuaJIT test with the stubbed SDK.
- Respect the vocabulary: **Dial Keys** (hold + wheel), **Nudge Keys** (tap
  −/+), **Command Keys**, **Combo Key** and **combos** ("Tab + K"), **System
  Keys**, **Palette** (with Lightroom's *Create New Mask* tools), **Key
  Reference**, **Value HUD**, **Keyboard** view, step and ⇧ step. For Lightroom
  features use Lightroom's own names (Sliders, Quick Develop, Constrain Crop,
  Add / Subtract / Intersect).
- UI text is English for now; translations are welcome.
- Be kind to Lightroom: never send events while it isn't the frontmost app, and
  keep everything on `127.0.0.1`.

### Original work only

Lumerist is an independent, original project built only on Adobe's public
Lightroom Classic SDK and public documentation. Please never contribute code,
assets, icons, text, screenshots or protocols taken from proprietary products,
and never decompile other software to write a contribution. If you are unsure,
ask in a discussion first.

By contributing you agree that your contribution is licensed under the
[GPL-3.0](LICENSE), like the rest of the project.
