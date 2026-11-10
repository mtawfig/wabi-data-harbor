#!/usr/bin/env bash
# Resource names of one Wabi Insight deployment on Azure, derived from the AZURE_NAME variable of the GitHub environment
# production-azure (deploy-azure.yml, docs/DEPLOYMENT_AZURE.md), appended to $GITHUB_ENV for the following steps.
# The Bicep template (deploy/azure/main.bicep) receives the same names.
set -euo pipefail

name="${AZURE_NAME:-}"
if [[ ! "$name" =~ ^[a-z0-9][a-z0-9-]{0,38}[a-z0-9]$ ]]; then
  echo "::error::Set the AZURE_NAME variable of the production-azure environment: 2-40 lowercase letters, digits and hyphens, for example wabi-insight-prod (docs/DEPLOYMENT_AZURE.md, steps 7 and 15)."
  exit 1
fi

# Storage account names allow lowercase letters and digits only (3-24).
storage="$(printf '%s' "$name" | tr -cd 'a-z0-9' | cut -c1-19)files"

{
  echo "WI_APP=$name"
  echo "WI_PLAN=$name-plan"
  echo "WI_SQL_SERVER=$name-sql"
  echo "WI_SQL_DATABASE=${AZURE_SQL_DATABASE:-WabiInsightDB}"
  echo "WI_STORAGE=$storage"
  echo "WI_APP_URL=https://$name.azurewebsites.net"
  echo "WI_PUBLIC_URL=${WI_PUBLIC_URL:-https://$name.azurewebsites.net}"
} >> "$GITHUB_ENV"

echo "App Service: $name · SQL server: $name-sql · database: ${AZURE_SQL_DATABASE:-WabiInsightDB} · storage: $storage"
