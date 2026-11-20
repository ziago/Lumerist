# Security policy

Lumerist reads keyboard, scroll and mouse events (on the Mac with the
Accessibility permission you grant it) and talks to its Lightroom Classic plug-in over
`127.0.0.1` only. If you find a way to abuse that — for example another
process sending commands to the plug-in, events leaking outside Lightroom, or a
problem with how the app or plug-in is installed — please report it privately.

## Reporting a vulnerability

Use GitHub's private reporting:
[**Report a vulnerability**](https://github.com/ziago/Lumerist/security/advisories/new).
Please do not open a public issue for security problems.

Include the Lumerist version, macOS or Windows version, Lightroom Classic version and the
steps to reproduce. You can expect a first answer within a week. Fixes are
released as soon as possible and credited in the changelog unless you prefer
otherwise.

## Verifying a download

Download Lumerist only from the
[releases page](https://github.com/ziago/Lumerist/releases). Every file there
has a `.sha256` file next to it.

- **Mac:** the DMG and the app are signed with a Developer ID and notarized by
  Apple; macOS checks them when you open them.
- **Windows:** the installer and the portable zip are **not code-signed yet**
  (signing will come in a later release), so Windows shows *Windows protected
  your PC* and *Unknown publisher* the first time. Compare the file's hash
  with the published one before running it:
  `Get-FileHash .\Lumerist-<version>-windows-x64-setup.exe` in PowerShell.

If a hash does not match, do not run the file, and please report it as above.

## Supported versions

Only the latest release receives security fixes.
