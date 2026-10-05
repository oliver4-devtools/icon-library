#!/usr/bin/env bash
# Runs the whole test suite. From the project folder:   ./tests/run-tests.sh
# Build the tool first (./package/package.sh) - the host tests use the DLLs it puts in bin/Release/net48.
# Optional: ./tests/run-tests.sh --no-ui   skips the browser tests.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
failed=0

echo "== 1/3 Catalogue build tests (Python) =="
python3 -m unittest discover -s tests/build || failed=1

echo
echo "== 2/3 Host tests (.NET) =="
(cd tests/HostTests && dotnet run -c Release) || failed=1

if [[ "${1:-}" != "--no-ui" ]]; then
  echo
  echo "== 3/3 UI tests (Playwright + Chromium) =="
  cd tests/ui
  if [[ ! -d node_modules ]]; then
    npm install --no-audit --no-fund && npx playwright install chromium || { echo "Could not install the UI test tools"; exit 1; }
  fi
  npx playwright test || failed=1
  cd "$ROOT"
fi

echo
if [[ $failed -eq 0 ]]; then echo "ALL TESTS PASSED"; else echo "SOME TESTS FAILED - see above"; fi
exit $failed
