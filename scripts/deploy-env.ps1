param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "test")]
    [string]$Env
)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host "Deploying app-platform environment '$Env'..."
npx cdk deploy --all -c "env=$Env" --require-approval never
