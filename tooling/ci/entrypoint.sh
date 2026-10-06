#!/bin/sh
# Copies the mounted repository (read-only at /src) to /work without host node_modules (host binaries
# do not run in Linux), installs dependencies from the lockfile and runs a gate stage (default: ci).
set -eu
mkdir -p /work
tar -C /src --exclude=./node_modules --exclude='*/node_modules' --exclude=./.local -cf - . | tar -C /work -xf -
cd /work
if [ -f pnpm-lock.yaml ]; then pnpm install --frozen-lockfile --reporter=silent; fi
for dir in ${NPM_DIRS:-${NPM_DIR:-}}; do
  if [ -f "$dir/package-lock.json" ]; then npm ci --prefix "$dir" --no-audit --no-fund; fi
done
if [ -n "${DOTNET_TARGET:-}" ] && command -v dotnet >/dev/null 2>&1; then dotnet tool restore && dotnet restore "$DOTNET_TARGET" --locked-mode; fi
exec node tooling/scripts/gate.mjs "$@" --strict
