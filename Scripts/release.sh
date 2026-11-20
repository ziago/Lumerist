#!/bin/zsh
# Builds, signs, notarizes and publishes a Lumerist release.
#
#   zsh Scripts/release.sh --dry-run    everything except notarization and upload
#   zsh Scripts/release.sh              full release as a GitHub *pre-release*
#   zsh Scripts/release.sh --final      full release, not marked as pre-release
#
# Options: --notary-profile NAME (default lumerist-notary), --repo OWNER/NAME
# (default ziago/Lumerist).
#
# One-time setup on the release Mac:
#   1. A "Developer ID Application" certificate in the login keychain.
#   2. Notarization credentials, stored once (the password is an app-specific
#      password from appleid.apple.com, typed at the prompt, never in files):
#        xcrun notarytool store-credentials lumerist-notary --apple-id <email> --team-id <TEAMID>
#   3. gh logged in with permission to create releases on the repository.
#
# The version comes from the top-level VERSION file; release notes come from
# the matching "## [x.y.z]" (or "## x.y.z") section of CHANGELOG.md.
# Output: build/release/<version>/ (git-ignored).
#
# Mac and Windows ship together. The Windows packages are built on Windows
# from the same commit (Scripts/build-windows-installer.ps1) and copied to
# build/release/<version>/windows/; a release refuses to publish without all
# of them (a dry run only warns).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
source "$root/Scripts/plugin-version.zsh"

dry_run=0
prerelease=1
profile="lumerist-notary"
repo="ziago/Lumerist"
while (( $# > 0 )); do
  case "$1" in
    --dry-run) dry_run=1 ;;
    --final) prerelease=0 ;;
    --notary-profile) shift; profile="${1:?--notary-profile needs a name}" ;;
    --repo) shift; repo="${1:?--repo needs OWNER/NAME}" ;;
    -h|--help) sed -n '2,22p' "$0"; exit 0 ;;
    *) print -u2 "Unknown option: $1"; exit 64 ;;
  esac
  shift
done

step() { print -P "%F{cyan}==>%f $*"; }
fail() { print -u2 -P "%F{red}error:%f $*"; exit 1; }
warn() { print -u2 -P "%F{yellow}warning:%f $*"; }

version="$(lumerist_version "$root")"
tag="v$version"
out="$root/build/release/$version"
app="$out/Lumerist.app"
zip="$out/Lumerist-$version.zip"
dmg="$out/Lumerist-$version.dmg"
notes="$out/release-notes.md"

# --- Preflight -------------------------------------------------------------
if (( dry_run )); then run_kind="dry run"; else run_kind="release"; fi
step "Lumerist $version → $repo ($run_kind)"

identity="${LUMERIST_SIGN_IDENTITY:-$(security find-identity -v -p codesigning 2>/dev/null | awk -F'"' '/Developer ID Application/ { print $2; exit }')}"
if [[ -z "$identity" ]]; then
  if (( dry_run )); then
    warn "No \"Developer ID Application\" identity: the dry run signs for development only."
  else
    fail "No \"Developer ID Application\" certificate in the keychain (needed for notarization)."
  fi
fi

if ! xcrun notarytool history --keychain-profile "$profile" >/dev/null 2>&1; then
  message="Notarization profile '$profile' is missing or not working. Create it once with:
    xcrun notarytool store-credentials $profile --apple-id <email> --team-id <TEAMID>"
  if (( dry_run )); then warn "$message"; else fail "$message"; fi
fi

mkdir -p "$out"
if [[ -f "$root/CHANGELOG.md" ]]; then
  # Literal match (index, not regex) on "## [x.y.z]" or "## x.y.z", so dots are
  # not wildcards and "1.0.0" never picks up a "1.0.0-beta" section.
  awk -v v="$version" '
    /^## / {
      if (found) exit
      heading = $0
      sub(/^## \[?/, "", heading)
      if (index(heading, v) == 1) {
        rest = substr(heading, length(v) + 1)
        if (rest ~ /^(\]|[ (]|$)/) { found = 1; next }
      }
    }
    found { print }
  ' "$root/CHANGELOG.md" > "$notes"
fi
if [[ ! -s "$notes" ]]; then
  message="CHANGELOG.md has no section for $version (expected a heading like '## [$version]')."
  if (( dry_run )); then warn "$message"; print "Lumerist $version" > "$notes"; else fail "$message"; fi
fi

if (( ! dry_run )); then
  gh auth status >/dev/null 2>&1 || fail "gh is not logged in (run: gh auth login)."
  gh release view "$tag" --repo "$repo" >/dev/null 2>&1 && fail "Release $tag already exists on $repo."
  # The published DMG must be built from exactly the commit the tag points to.
  git -C "$root" rev-parse --is-inside-work-tree >/dev/null 2>&1 || fail "Not a git repository."
  [[ -z "$(git -C "$root" status --porcelain)" ]] || fail "Uncommitted changes: commit or stash them first."
  git -C "$root" fetch --quiet --tags || fail "git fetch failed."
  upstream="$(git -C "$root" rev-parse '@{u}' 2>/dev/null)" || fail "The current branch has no upstream: push it first."
  [[ "$(git -C "$root" rev-parse HEAD)" == "$upstream" ]] || fail "HEAD is not pushed (or is behind its upstream)."
  [[ -z "$(git -C "$root" ls-remote --tags origin "refs/tags/$tag")" ]] || fail "Tag $tag already exists on the remote."
  head_commit="$(git -C "$root" rev-parse HEAD)"
fi

# --- Windows packages (built on Windows, same version and commit) -----------
windows="$out/windows"
windows_assets=()
for arch in x64 arm64; do
  for file in "Lumerist-$version-windows-$arch-setup.exe" "Lumerist-$version-windows-$arch.zip"; do
    windows_assets+=("$windows/$file" "$windows/$file.sha256")
  done
done
missing=()
for file in "${windows_assets[@]}"; do
  [[ -f "$file" ]] || missing+=("${file#$out/}")
done
if (( ${#missing} )); then
  message="Windows packages missing: ${missing[*]}
  Build them on Windows (Scripts/build-windows-installer.ps1) and copy its build/release/$version/windows folder to $windows."
  if (( dry_run )); then warn "$message"; windows_assets=(); else fail "$message"; fi
elif (( ! dry_run )); then
  [[ "$(cat "$windows/BUILD-COMMIT" 2>/dev/null)" == "$head_commit" ]] \
    || fail "The Windows packages were not built from $head_commit (see $windows/BUILD-COMMIT)."
fi
if (( ${#windows_assets} )); then
  ( cd "$windows" && shasum -a 256 -c -- *.sha256 >/dev/null ) || fail "A Windows package does not match its checksum."
  step "Windows packages: ${#windows_assets} files, checksums verified"
fi

# --- Build ------------------------------------------------------------------
rm -rf "$out/Lumerist.app" "$zip" "$dmg" "$dmg.sha256"
mkdir -p "$out"
if [[ -n "$identity" ]]; then
  step "Universal build, Developer ID signature, hardened runtime"
  LUMERIST_SIGN_IDENTITY="$identity" zsh "$root/Scripts/build-macos-app.sh" --release --output "$out"
else
  step "Universal build (development signature: dry run only)"
  zsh "$root/Scripts/build-macos-app.sh" --universal --output "$out"
fi

step "Checking the app"
codesign --verify --deep --strict --verbose=2 "$app"
lipo -archs "$app/Contents/MacOS/LumeristApp"
codesign -dv "$app" 2>&1 | grep -E "Authority=|flags=|Timestamp=" || true
[[ -f "$app/Contents/Resources/Lumerist.lrplugin/Info.lua" ]] || fail "The plug-in is missing from the app bundle."

# --- Notarize the app -----------------------------------------------------------
notarize() {
  local file="$1"
  local result
  # "|| true": a rejected submission must still reach the log below.
  result="$(xcrun notarytool submit "$file" --keychain-profile "$profile" --wait --output-format plist)" || true
  # Not "status": zsh reserves that name (the last exit code) and refuses to assign it.
  local outcome submission
  outcome="$(print -r -- "$result" | plutil -extract status raw -o - - 2>/dev/null || true)"
  submission="$(print -r -- "$result" | plutil -extract id raw -o - - 2>/dev/null || true)"
  if [[ "$outcome" != "Accepted" ]]; then
    [[ -n "$submission" ]] && xcrun notarytool log "$submission" --keychain-profile "$profile" || true
    fail "Notarization of $(basename "$file") ended with status: ${outcome:-unknown}"
  fi
}

ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
if (( dry_run )); then
  step "Dry run: skipping notarization and stapling of the app"
else
  step "Notarizing the app (this can take a few minutes)"
  notarize "$zip"
  xcrun stapler staple "$app"
  xcrun stapler validate "$app"
fi

# --- Disk image ------------------------------------------------------------
step "Building the disk image"
staging="$(mktemp -d "${TMPDIR:-/tmp}/lumerist-dmg.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
ditto "$app" "$staging/Lumerist.app"
ln -s /Applications "$staging/Applications"
hdiutil create -volname "Lumerist $version" -srcfolder "$staging" -fs HFS+ -format UDZO -ov "$dmg" >/dev/null
if [[ -n "$identity" ]]; then
  codesign --force --timestamp --sign "$identity" "$dmg"
fi
if (( dry_run )); then
  step "Dry run: skipping notarization and stapling of the disk image"
else
  step "Notarizing the disk image"
  notarize "$dmg"
  xcrun stapler staple "$dmg"
  xcrun stapler validate "$dmg"
  spctl --assess --type open --context context:primary-signature --verbose "$dmg"
fi

( cd "$out" && shasum -a 256 "$(basename "$dmg")" > "$(basename "$dmg").sha256" )

# --- Publish ---------------------------------------------------------------
release_flags=(--title "Lumerist $version" --notes-file "$notes" --repo "$repo")
(( prerelease )) && release_flags+=(--prerelease)
if (( dry_run )); then
  step "Dry run complete. A real run would publish:"
  print "  gh release create $tag ${(q)dmg} ${(q)dmg}.sha256 ${(q)windows_assets[@]} ${release_flags[*]}"
else
  # Created as a draft on the built commit, checked, and only then published,
  # so a half-uploaded release is never visible to users.
  step "Creating draft release $tag on GitHub"
  gh release create "$tag" "$dmg" "$dmg.sha256" "${windows_assets[@]}" "${release_flags[@]}" --target "$head_commit" --draft
  assets="$(gh release view "$tag" --repo "$repo" --json assets --jq '.assets[].name')"
  for expected in "$(basename "$dmg")" "$(basename "$dmg").sha256" "${windows_assets[@]:t}"; do
    print -r -- "$assets" | grep -qxF "$expected" || fail "Draft $tag is missing $expected; fix it on GitHub before publishing."
  done
  step "Publishing $tag"
  gh release edit "$tag" --repo "$repo" --draft=false
  gh release view "$tag" --repo "$repo" --json url --jq .url
fi

print
print "Artifacts in $out:"
ls -lh "$out" | sed 1d
