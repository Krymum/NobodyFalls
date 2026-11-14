#!/usr/bin/env bash
# Runs the responsive tests (390 × 844, 820 × 1180, 1440 × 900) in the official Playwright
# Linux image, against the stack on port 8080 (`docker compose up`). Reference screenshots
# are made and compared there, so that fonts render the same locally and in CI.
#   scripts/screenshots.sh            compare with e2e/__screenshots__
#   scripts/screenshots.sh --update   regenerate the references
set -euo pipefail

cd "$(dirname "$0")/.."
version=$(node -p "require('./package.json').devDependencies['@playwright/test']")
pnpm_version=$(node -p "require('./package.json').packageManager.split('@')[1]")
base_url=${E2E_BASE_URL:-http://host.docker.internal:8080}
update=""
if [[ "${1:-}" == "--update" ]]; then
  update="--update-snapshots"
fi

mkdir -p e2e/__screenshots__ test-results
# MSYS_NO_PATHCONV: keep container paths intact under Git Bash on Windows.
MSYS_NO_PATHCONV=1 docker run --rm --ipc=host \
  --add-host=host.docker.internal:host-gateway \
  -e CI="${CI:-}" -e E2E_BASE_URL="$base_url" -e E2E_SCREENSHOTS=1 \
  -v "$(pwd)":/src:ro \
  -v "$(pwd)/e2e/__screenshots__":/out \
  -v "$(pwd)/test-results":/results \
  "mcr.microsoft.com/playwright:v${version}-noble" \
  bash -c "
    set -e
    mkdir -p /work && cd /src
    tar --exclude=./node_modules --exclude='./*/*/node_modules' --exclude=./test-results \
        --exclude=./playwright-report -cf - . | tar -xf - -C /work
    cd /work && npm install --global --silent pnpm@$pnpm_version && CI=1 pnpm install --frozen-lockfile --silent
    status=0
    pnpm exec playwright test --project=phone-390 --project=tablet-820 --project=desktop-1440 \
      --output=/results $update || status=\$?
    if [[ -n '$update' ]]; then cp -r e2e/__screenshots__/. /out/; fi
    exit \$status
  "
