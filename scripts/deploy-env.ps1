param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "test")]
    [string]$Env,

    [Parameter()]
    [string]$App
)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$knownApps = @("ConstFlow", "ProFlow", "WaterFlow")

function Resolve-PlatformApp {
    param([string]$Name)

    foreach ($known in $knownApps) {
        if ($known.Equals($Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $known
        }
    }

    throw "App must be one of: $($knownApps -join ', ')."
}

function Get-StackOutput {
    param(
        [string]$StackName,
        [string]$Key,
        [string]$Region
    )

    $value = aws cloudformation describe-stacks `
        --region $Region `
        --stack-name $StackName `
        --query "Stacks[0].Outputs[?OutputKey=='$Key'].OutputValue | [0]" `
        --output text
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value) -or $value -eq "None") {
        throw "Stack $StackName did not publish $Key."
    }

    return $value.Trim()
}

function Publish-AppFrontend {
    param(
        [string]$AppName,
        [string]$EnvName,
        [string]$Region
    )

    $webDir = Join-Path (Split-Path (Get-Location) -Parent) "$AppName\web"
    if (-not (Test-Path -LiteralPath (Join-Path $webDir "package.json"))) {
        Write-Host "No web folder for $AppName at $webDir; skipping frontend publish."
        return
    }

    $prefix = "/$($AppName.ToLowerInvariant())"
    Write-Host "Building $AppName web at $webDir ($prefix)"
    Push-Location -LiteralPath $webDir
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) {
            throw "npm ci failed in $webDir."
        }

        $env:VITE_API_BASE_URL = $prefix
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build failed in $webDir."
        }
    }
    finally {
        Remove-Item Env:VITE_API_BASE_URL -ErrorAction SilentlyContinue
        Pop-Location
    }

    $dist = Join-Path $webDir "dist"
    if (-not (Test-Path -LiteralPath $dist)) {
        throw "Build did not produce $dist."
    }

    $envLabel = $EnvName.Substring(0, 1).ToUpperInvariant() + $EnvName.Substring(1)
    $stackName = "AppPlatform-$envLabel-$AppName"
    $bucket = Get-StackOutput -StackName $stackName -Key "FrontendBucket" -Region $Region
    $distributionId = Get-StackOutput -StackName $stackName -Key "FrontendDistributionId" -Region $Region

    Write-Host "Uploading $dist to s3://$bucket"
    aws s3 sync "$dist" "s3://$bucket" --delete --region $Region
    if ($LASTEXITCODE -ne 0) {
        throw "s3 sync failed for $bucket."
    }

    aws cloudfront create-invalidation --distribution-id $distributionId --paths "/*"
    if ($LASTEXITCODE -ne 0) {
        throw "CloudFront invalidation failed for $distributionId."
    }

    Write-Host "Published $AppName frontend."
}

$cdkArgs = @("-c", "env=$Env")
$apps = $knownApps
if ($App) {
    $apps = @(Resolve-PlatformApp $App)
    $cdkArgs += @("-c", "app=$($apps[0])")
    Write-Host "Deploying app '$($apps[0])' in environment '$Env' (shared network is included)..."
}
else {
    Write-Host "Deploying app-platform environment '$Env' (all apps)..."
}

npx cdk deploy --all @cdkArgs --require-approval never
if ($LASTEXITCODE -ne 0) {
    throw "cdk deploy failed."
}

$region = $env:CDK_DEFAULT_REGION
if ([string]::IsNullOrWhiteSpace($region)) {
    $region = $env:AWS_REGION
}
if ([string]::IsNullOrWhiteSpace($region)) {
    $region = "us-west-1"
}

foreach ($appName in $apps) {
    Publish-AppFrontend -AppName $appName -EnvName $Env -Region $region
}
