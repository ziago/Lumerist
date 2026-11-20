## What does this change?

<!-- A short description and the reason for the change. Link the issue or discussion. -->

## How was it tested?

- [ ] `swift test`
- [ ] `dotnet test Windows`
- [ ] `zsh Scripts/test-lua.sh`
- [ ] `python3 Scripts/generate-parameters.py --check`
- [ ] `python3 Scripts/check-parity.py`
- [ ] Tried in Lightroom Classic on macOS (Lightroom version: …, macOS: …)
- [ ] Tried in Lightroom Classic on Windows (Lightroom version: …, Windows: …)

## Both platforms

- [ ] The change is in both apps (`Sources/` and `Windows/`), or only the plug-in.
- [ ] Shared behaviour has a case in `Shared/fixtures`.
- [ ] If one side is missing: explained here, so a `parity` issue can be opened.

## Checklist

- [ ] New behaviour is covered by tests.
- [ ] UI text uses the Lumerist vocabulary (Dial / Nudge / Command Keys, Combo Key, Palette, Key Reference, Value HUD).
- [ ] This contribution is my own work and contains no code, assets, text or protocols taken from proprietary products.
