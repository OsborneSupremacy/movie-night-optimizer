#!/usr/bin/env bash
# Builds and deploys everything from this machine: the Lambda, the infrastructure, the site.
# Needs dotnet, terraform, node and the AWS CLI, with credentials for the target account.
#
# Any arguments go to terraform apply. To divert every email to one inbox while building:
#   ./scripts/deploy.sh -var live_mode=false -var test_recipient=someone@example.com
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TERRAFORM_DIR="${REPO_ROOT}/iac/terraform"

# The zip has to exist before Terraform plans, because the function's source hash is read from it.
"${SCRIPT_DIR}/build-lambdas.sh"

terraform -chdir="${TERRAFORM_DIR}" init -input=false
terraform -chdir="${TERRAFORM_DIR}" apply "$@"

pushd "${REPO_ROOT}/src/app" >/dev/null
npm ci
npm run build
popd >/dev/null

BUCKET="$(terraform -chdir="${TERRAFORM_DIR}" output -raw frontend_bucket)"
DISTRIBUTION_ID="$(terraform -chdir="${TERRAFORM_DIR}" output -raw cloudfront_distribution_id)"

aws s3 sync "${REPO_ROOT}/src/app/dist/" "s3://${BUCKET}" --delete

aws cloudfront create-invalidation \
  --distribution-id "${DISTRIBUTION_ID}" \
  --paths "/*" \
  --query 'Invalidation.Id' \
  --output text

echo "Deployed to $(terraform -chdir="${TERRAFORM_DIR}" output -raw site_url)"
