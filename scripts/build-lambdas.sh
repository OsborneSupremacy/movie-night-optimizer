#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

FRAMEWORK="net10.0"
RUNTIME="linux-arm64"
CONFIGURATION="Release"

# ReadyToRun compiles ahead of time, so a cold start doesn't spend its first seconds JIT-compiling
# the SDK and the DI container. It roughly doubles the zip, which costs nothing that matters here.
build_lambda() {
  local project_dir="$1"
  local zip_name="$2"

  echo "Building ${project_dir}/${zip_name}..."

  pushd "${project_dir}" >/dev/null

  dotnet publish \
    -o bin/publish \
    -c "${CONFIGURATION}" \
    --framework "${FRAMEWORK}" \
    /p:GenerateRuntimeConfigurationFiles=true \
    --runtime "${RUNTIME}" \
    --self-contained false \
    /p:PublishReadyToRun=true

  pushd bin/publish >/dev/null
  rm -f "../${zip_name}"
  zip -rq "../${zip_name}" .
  popd >/dev/null

  popd >/dev/null
}

build_lambda "${REPO_ROOT}/src/MovieNight.Library" "MovieNight.Library.zip"
