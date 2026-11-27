# Security policy

Lumerist reads keyboard, mouse and trackpad events (on the Mac with the
Accessibility permission you grant it) and talks to its Lightroom Classic
plug-in over `127.0.0.1` only. Every request carries a secret that only your
own account can read, and the plug-in refuses anything without it (see
[the bridge token](docs/protocol-v4.md#the-bridge-token)). If you find a way to
abuse that — for example another account or a web page sending commands to
the plug-in, events leaking outside Lightroom, or a problem with how the app
or plug-in is installed — please report it privately.

## Reporting a vulnerability

Use GitHub's private reporting:
[**Report a vulnerability**](https://github.com/ziago/Lumerist/security/advisories/new).
Please do not open a public issue for security problems.

Include the Lumerist version, macOS or Windows version, Lightroom Classic
version and the steps to reproduce. You can expect a first answer within a
week. Fixes are released as soon as possible and credited in the changelog
unless you prefer otherwise.

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

From 0.9.2 every release also has `SHA256SUMS`, the hashes of all its
packages, and `SHA256SUMS.sig`, their signature with Lumerist's update key.
The key is kept off GitHub, and the app checks this signature before it
updates itself. To check it by hand on a Mac:

```sh
echo MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEt37ZfOYd5bo4/ZKP73l2KTyzgZI9Ih0nlBof4WjmWuaoFd3JYfLtZah8HT6R2DP62EYmSxeW2N25UIN1DIhzVA== \
  | base64 -d | openssl pkey -pubin -inform DER > lumerist-update.pem
base64 -d < SHA256SUMS.sig > SHA256SUMS.der
openssl dgst -sha256 -verify lumerist-update.pem -signature SHA256SUMS.der SHA256SUMS
shasum -a 256 -c SHA256SUMS --ignore-missing
```

If a hash does not match, do not run the file, and please report it as above.

**Update now**, in Lumerist's update window, does these checks by itself
before installing anything. It downloads only from GitHub over https, and
the file must match `SHA256SUMS`, the release's list of hashes, whose
signature in `SHA256SUMS.sig` must check out against the public key built
into Lumerist. The private key never leaves the developer's Mac: it is not
on GitHub or in the build service, so someone who could publish a release
still could not make Lumerist install it. On the Mac the app must also carry
Lumerist's Developer ID signature and Apple's notarization, and be the
announced version. If any check fails, nothing is installed and the current
version stays as it is.

You can check the list yourself:
`openssl dgst -sha256 -verify lumerist-update.pub -signature <(base64 -d < SHA256SUMS.sig) SHA256SUMS`,
with the public key
`MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEt37ZfOYd5bo4/ZKP73l2KTyzgZI9Ih0nlBof4WjmWuaoFd3JYfLtZah8HT6R2DP62EYmSxeW2N25UIN1DIhzVA==`
(base64 DER; save it as PEM between `-----BEGIN PUBLIC KEY-----` lines).

## Supported versions

Only the latest release receives security fixes.
