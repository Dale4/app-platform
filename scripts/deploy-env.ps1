param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "test")]
    [string]$Env,

    [Parameter()]
    [string]$App
)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$cdkArgs = @("-c", "env=$Env")
if ($App) {
    $cdkArgs += @("-c", "app=$App")
    Write-Host "Deploying app '$App' in environment '$Env' (shared network is included)..."
}
else {
    Write-Host "Deploying app-platform environment '$Env' (all apps)..."
}

npx cdk deploy --all @cdkArgs --require-approval never
