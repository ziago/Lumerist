#!/bin/zsh
# Developer tool: copies the plug-in from this checkout into Lightroom's
# Modules folder. End users install it from the Lumerist app instead
# (first-run guide, or General → Lightroom plug-in).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
source "$root/Scripts/plugin-version.zsh"
source_plugin="$root/Lightroom/Lumerist.lrplugin"
# LUMERIST_MODULES_DIR is only for isolated development/test staging.
modules_dir="${LUMERIST_MODULES_DIR:-$HOME/Library/Application Support/Adobe/Lightroom/Modules}"
destination="$modules_dir/Lumerist.lrplugin"
# Backups live OUTSIDE Modules: Lightroom scans that folder, and plug-ins that
# share a toolkit identifier disable each other.
if [[ -n "${LUMERIST_MODULES_DIR:-}" ]]; then
  default_backup_dir="$modules_dir/../Lumerist-plugin-backups"
else
  default_backup_dir="$HOME/Library/Application Support/Lumerist/plugin-backups"
fi
backup_dir="${LUMERIST_BACKUP_DIR:-$default_backup_dir}"

if [[ ! -d "$source_plugin" ]]; then
  print -u2 "Source plug-in is missing: $source_plugin"
  exit 1
fi

if [[ -e "$destination" && "${1:-}" != "--replace" ]]; then
  print -u2 "Refusing to overwrite $destination. Re-run with --replace after closing Lightroom Classic."
  exit 2
fi

if [[ "${1:-}" == "--replace" ]]; then
  # Only the real Modules folder matters to a running Lightroom.
  if [[ -z "${LUMERIST_MODULES_DIR:-}" ]] && pgrep -x "Adobe Lightroom Classic" >/dev/null; then
    print -u2 "Close Lightroom Classic before replacing its plug-in."
    exit 3
  fi
  mkdir -p "$backup_dir"
  backup="$backup_dir/Lumerist.lrplugin.backup-$(date +%Y%m%d-%H%M%S)"
  mv "$destination" "$backup"
  print "Moved existing plug-in to recoverable backup: $backup"
fi

mkdir -p "$modules_dir"
ditto "$source_plugin" "$destination"
find "$destination" -name .DS_Store -delete
# Same version stamp as the copy inside the app, so the app's installer agrees.
stamp_plugin_version "$destination/Info.lua" "$(lumerist_version "$root")"
print "Installed development plug-in at: $destination"
print "Restart Lightroom Classic to load it (it keeps plug-in code in memory)."

