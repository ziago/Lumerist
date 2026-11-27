#!/bin/zsh
# Builds Lumerist.app with the Lightroom plug-in inside it.
#
#   zsh Scripts/build-macos-app.sh               host architecture, development signing
#   zsh Scripts/build-macos-app.sh --universal   Apple Silicon + Intel
#   zsh Scripts/build-macos-app.sh --release     universal + Developer ID + hardened runtime + timestamp
#   zsh Scripts/build-macos-app.sh --debug       debug configuration: the LUMERIST_* test overrides work (never for a release)
#   ... --output DIR                             where Lumerist.app goes (default: build/)
#
# Development builds are signed with your first "Apple Development" identity so
# macOS keeps the Accessibility permission across rebuilds (an ad-hoc signature
# changes every build). Release builds need a "Developer ID Application"
# identity, required for notarization.
#   LUMERIST_SIGN_IDENTITY   override the identity ("-" = ad-hoc, development only)
#   LUMERIST_BUILD_NUMBER    CFBundleVersion (default: date and time)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
source "$root/Scripts/plugin-version.zsh"

mode="development"
configuration="release"
universal=0
out_dir="$root/build"
while (( $# > 0 )); do
  case "$1" in
    --universal) universal=1 ;;
    --release) mode="release"; universal=1 ;;
    --debug) configuration="debug" ;;
    --output) shift; out_dir="${1:?--output needs a folder}" ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) print -u2 "Unknown option: $1"; exit 64 ;;
  esac
  shift
done

if [[ "$mode" == "release" && "$configuration" == "debug" ]]; then
  print -u2 -r -- "--debug builds honour the LUMERIST_* test overrides: never use one for a release."
  exit 64
fi

version="$(lumerist_version "$root")"
build_number="${LUMERIST_BUILD_NUMBER:-$(date +%Y%m%d%H%M)}"

# Signing identity, chosen before building so a missing one fails fast.
identity="${LUMERIST_SIGN_IDENTITY:-}"
if [[ "$mode" == "release" ]]; then
  if [[ -z "$identity" ]]; then
    identity="$(security find-identity -v -p codesigning 2>/dev/null | awk -F'"' '/Developer ID Application/ { print $2; exit }')"
  fi
  if [[ -z "$identity" || "$identity" == "-" || "$identity" != *"Developer ID Application"* ]]; then
    print -u2 "A release build needs a \"Developer ID Application\" certificate in your keychain."
    print -u2 "Create it at developer.apple.com → Certificates (or in Xcode → Settings → Accounts), then retry."
    exit 4
  fi
elif [[ -z "$identity" ]]; then
  identity="$(security find-identity -v -p codesigning 2>/dev/null | awk -F'"' '/Apple Development/ { print $2; exit }')"
fi

cd "$root"
if (( universal )); then
  arch_flags=(--arch arm64 --arch x86_64)
else
  arch_flags=()
fi
swift build -c "$configuration" "${arch_flags[@]}"
bin_dir="$(swift build -c "$configuration" "${arch_flags[@]}" --show-bin-path)"
binary="$bin_dir/LumeristApp"
archs="$(lipo -archs "$binary")"
if (( universal )) && [[ "$archs" != *arm64* || "$archs" != *x86_64* ]]; then
  print -u2 "Expected a universal binary (arm64 + x86_64), got: $archs"
  exit 5
fi

app="$out_dir/Lumerist.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/Fonts"

cp "$root/Resources/Info.plist" "$app/Contents/Info.plist"
set_plist_string() {
  /usr/libexec/PlistBuddy -c "Set :$1 $2" "$app/Contents/Info.plist" 2>/dev/null \
    || /usr/libexec/PlistBuddy -c "Add :$1 string $2" "$app/Contents/Info.plist"
}
set_plist_string CFBundleShortVersionString "$version"
set_plist_string CFBundleVersion "$build_number"

cp "$binary" "$app/Contents/MacOS/LumeristApp"
# App icons (rebuild both with: swift Scripts/render-icon.swift): the app's
# icon, in the Galerist theme, and the Dock icon used while the Miami theme is on.
cp "$root/Resources/Lumerist.icns" "$app/Contents/Resources/Lumerist.icns"
cp "$root/Resources/Lumerist-Miami.icns" "$app/Contents/Resources/Lumerist-Miami.icns"
# SIL OFL fonts (Chakra Petch, Inter, JetBrains Mono) and their licences.
cp "$root"/Resources/Fonts/*.ttf "$root"/Resources/Fonts/*-OFL.txt "$app/Contents/Resources/Fonts/"

# The Lightroom plug-in travels inside the app; the app installs it from
# General → Lightroom plug-in or the first-run guide.
plugin="$app/Contents/Resources/Lumerist.lrplugin"
ditto "$root/Lightroom/Lumerist.lrplugin" "$plugin"
find "$plugin" -name .DS_Store -delete
stamp_plugin_version "$plugin/Info.lua" "$version"

if [[ "$mode" == "release" ]]; then
  codesign --force --options runtime --timestamp --sign "$identity" "$app"
elif [[ -n "$identity" && "$identity" != "-" ]]; then
  codesign --force --options runtime --sign "$identity" "$app"
else
  identity="ad-hoc"
  codesign --force --options runtime --sign - "$app"
  print "Signed ad-hoc: macOS will ask again for Accessibility after each rebuild."
fi
codesign --verify --deep --strict "$app"

print "Created $app"
print "  version $version ($build_number) · $archs · $mode · $configuration configuration · signed with: $identity"
