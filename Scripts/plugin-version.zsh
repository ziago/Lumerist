# Shared helpers for the build and install scripts (source this file).

# Prints the version from the top-level VERSION file, or fails.
lumerist_version() {
  local root="$1"
  local version
  version="$(tr -d '[:space:]' < "$root/VERSION" 2>/dev/null || true)"
  if [[ ! "$version" =~ '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$' ]]; then
    print -u2 "VERSION must contain a version like 0.9.0 (found: '${version:-nothing}')."
    return 1
  fi
  print -- "$version"
}

# Rewrites the VERSION table of a plug-in Info.lua so Plug-in Manager and the
# app's installer show the same version as the app. Keeps the protocol number.
#   stamp_plugin_version <path/to/Info.lua> <version>
stamp_plugin_version() {
  local info="$1" version="$2"
  local core="${version%%-*}"
  local -a parts
  parts=("${(@s:.:)core}")
  local protocol
  protocol="$(grep -o 'protocol [0-9][0-9]*' "$info" | head -1 | grep -o '[0-9][0-9]*' || true)"
  local display="$version"
  [[ -n "$protocol" ]] && display="$version (protocol $protocol)"
  if ! grep -q 'VERSION[[:space:]]*=[[:space:]]*{' "$info"; then
    print -u2 "No VERSION table in $info; version not stamped."
    return 1
  fi
  # -0: the whole file at once, so a VERSION table split over lines matches too.
  MAJOR="${parts[1]}" MINOR="${parts[2]}" PATCH="${parts[3]}" DISPLAY="$display" \
    perl -0pi -e 's/^([ \t]*)VERSION\s*=\s*\{[^}]*\}\s*,?/$1VERSION = { major = $ENV{MAJOR}, minor = $ENV{MINOR}, revision = $ENV{PATCH}, display = "$ENV{DISPLAY}" },/m' "$info"
  # Never ship a plug-in whose version silently differs from the app's.
  if ! grep -qF "display = \"$display\"" "$info"; then
    print -u2 "Could not stamp version $display into $info."
    return 1
  fi
}
