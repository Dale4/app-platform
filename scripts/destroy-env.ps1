param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "test")]
    [string]$Env,

    [Parameter()]
    [string]$App
)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

if ($App) {
    Write-Host "Destroying app '$App' in environment '$Env' (shared network is kept)..."
    $listed = @(npx cdk ls -c "env=$Env" -c "app=$App")
    $appStacks = @($listed | Where-Object { $_ -match '\S' -and $_ -notmatch '-Network$' })
    if ($appStacks.Count -eq 0) {
        throw "No app stack found for '$App' in environment '$Env'."
    }
    npx cdk destroy @appStacks -c "env=$Env" -c "app=$App" --force
}
else {
    Write-Host "Destroying app-platform environment '$Env'..."
    npx cdk destroy --all -c "env=$Env" --force
}
