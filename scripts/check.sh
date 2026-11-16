#!/usr/bin/env bash
# Builds and checks everything on this machine: the API's tests, the Lambda zip, the site and the
# Terraform. deploy.sh runs it first; run it on its own before merging a branch into main.
# Needs dotnet, terraform and node. It touches nothing in AWS.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TERRAFORM_DIR="${REPO_ROOT}/iac/terraform"

dotnet test "${REPO_ROOT}"

# The zip has to exist before Terraform validates or plans, because the function's source hash is
# read from it.
"${SCRIPT_DIR}/build-lambdas.sh"

pushd "${REPO_ROOT}/src/app" >/dev/null
npm ci
npm run build
popd >/dev/null

terraform -chdir="${TERRAFORM_DIR}" fmt -check -recursive

# Validating needs providers but not the state backend. A throwaway data directory keeps this
# backend-less init away from the real one in iac/terraform/.terraform.
TF_DATA_DIR="$(mktemp -d)"
export TF_DATA_DIR
trap 'rm -rf "${TF_DATA_DIR}"' EXIT
terraform -chdir="${TERRAFORM_DIR}" init -backend=false -input=false >/dev/null
terraform -chdir="${TERRAFORM_DIR}" validate
