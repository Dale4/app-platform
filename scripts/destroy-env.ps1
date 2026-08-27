param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "test")]
    [string]$Env
)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host "Destroying app-platform environment '$Env'..."
npx cdk destroy --all -c "env=$Env" --force
