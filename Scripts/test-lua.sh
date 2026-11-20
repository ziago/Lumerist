#!/bin/zsh
# Runs the Lightroom plug-in logic tests under LuaJIT (Lua 5.1, like Lightroom).
# Lightroom itself is never contacted: the SDK namespaces are stubbed.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if ! command -v luajit >/dev/null; then
  print -u2 "luajit is required: brew install luajit"
  exit 2
fi

luajit Tests/Lua/run.lua
