#!/usr/bin/env bash
# Stop-hook build check.
#
# Exits 2 (blocking, feeds output back to Claude) only for genuine compile errors.
# A locked-output failure is NOT a broken build: when the API is running under `dotnet run`
# or Visual Studio holds the DLLs, MSBuild compiles fine and then fails at the copy step with
# MSB3021/MSB3026/MSB3027. Blocking on that would mean the hook fails every time the app is
# up, so those are reported as a note instead.
set -uo pipefail

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

out=$(dotnet build --nologo -v q 2>&1) && exit 0

# Distinct MSBuild/Roslyn error codes in this run, minus the file-lock family.
real_errors=$(
  printf '%s\n' "$out" \
    | grep -oE 'error [A-Z]+[0-9]+' \
    | sort -u \
    | grep -vE 'error MSB30(21|26|27)$'
)

if [ -z "$real_errors" ]; then
  locker=$(printf '%s\n' "$out" | grep -oE 'locked by: "[^"]+"' | head -1 | sed 's/locked by: //' | tr -d '"')
  printf '{"systemMessage":"Build check: compile OK. Output files are locked by %s, so the copy step failed (MSB3021/3027). Stop the app to get a full build."}\n' \
    "${locker:-a running process}"
  exit 0
fi

printf '%s\n' "$out" >&2
exit 2
