#!/usr/bin/env bash
# Builds, checks and deploys everything from this machine: the Lambda, the infrastructure, the
# site. There is no pipeline; this script is the only way anything reaches AWS.
# Needs dotnet, terraform, node and the AWS CLI, with credentials for the target account.
#
# Any arguments go to terraform apply. To divert every email to one inbox while building:
#   ./scripts/deploy.sh -var live_mode=false -var test_recipient=someone@example.com
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TERRAFORM_DIR="${REPO_ROOT}/iac/terraform"

# Tests, the Lambda zip, the site build and the Terraform checks. Nothing deploys if any fail.
"${SCRIPT_DIR}/check.sh"

terraform -chdir="${TERRAFORM_DIR}" init -input=false
terraform -chdir="${TERRAFORM_DIR}" apply "$@"

BUCKET="$(terraform -chdir="${TERRAFORM_DIR}" output -raw frontend_bucket)"
DISTRIBUTION_ID="$(terraform -chdir="${TERRAFORM_DIR}" output -raw cloudfront_distribution_id)"

aws s3 sync "${REPO_ROOT}/src/app/dist/" "s3://${BUCKET}" --delete

aws cloudfront create-invalidation \
  --distribution-id "${DISTRIBUTION_ID}" \
  --paths "/*" \
  --query 'Invalidation.Id' \
  --output text

echo "Deployed to $(terraform -chdir="${TERRAFORM_DIR}" output -raw site_url)"
