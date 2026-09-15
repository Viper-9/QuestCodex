<#
.SYNOPSIS
  Builds the QuestCodex web app + server mod, packages them into a zip with the
  SPT_Runtime/user/mods/QuestCodex layout, and deploys into a local SPT install.

.USAGE
  .\tools\package.ps1                       full build + zip + deploy
  .\tools\package.ps1 -SkipWeb              reuse existing server/wwwroot (no npm)
  .\tools\package.ps1 -SkipBuild            reuse existing server/bin/Release output
  .\tools\package.ps1 -SkipDeploy           zip only
  .\tools\package.ps1 -SptRuntimeDir "D:\Other\SPT_Runtime"
#>

param(
    [switch]$SkipWeb,
    [switch]$SkipBuild,
    [switch]$SkipDeploy,
    [string]$SptRuntimeDir = "F:\SPT4.1.2\SPT_Runtime"
)

$ErrorActionPreference = "Stop"

$ModName       = "QuestCodex"
$RootDir       = Split-Path $PSScriptRoot -Parent
$ServerProjDir = Join-Path $RootDir "server"
$WebDir        = Join-Path $RootDir "web"
$WebDist       = Join-Path $WebDir "dist"
$WwwrootDir    = Join-Path $ServerProjDir "wwwroot"
$BuildOut      = Join-Path $ServerProjDir "bin\Release\$ModName"
$DistDir       = Join-Path $RootDir "dist"
$StageDir      = Join-Path $DistDir "SPT_Runtime\user\mods\$ModName"
$DeployDir     = Join-Path $SptRuntimeDir "user\mods\$ModName"

# Version: single source of truth is Directory.Build.props.
$PropsPath = Join-Path $RootDir "Directory.Build.props"
$VersionMatch = Select-String -Path $PropsPath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $VersionMatch) { throw "Could not find <Version> in $PropsPath" }
$ModVersion = $VersionMatch.Matches[0].Groups[1].Value
$ZipPath    = Join-Path $RootDir "$ModName-$ModVersion.zip"

# Files never shipped: symbols, deps manifest, static-web-assets metadata.
$ExcludePatterns = @('*.pdb', '*.deps.json', '*.staticwebassets.*.json')

# ---------------------------------------------------------------- web
if (-not $SkipWeb) {
    Write-Host "==> npm build ($WebDir)" -ForegroundColor Cyan
    Push-Location $WebDir
    try {
        if (-not (Test-Path (Join-Path $WebDir "node_modules"))) {
            npm ci
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed (exit code $LASTEXITCODE)" }
        }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed (exit code $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }

    if (-not (Test-Path (Join-Path $WebDist ".vite\manifest.json"))) {
        throw "web/dist/.vite/manifest.json missing - vite build.manifest must be enabled"
    }

    Write-Host "==> Copying web/dist -> server/wwwroot" -ForegroundColor Cyan
    if (Test-Path $WwwrootDir) { Remove-Item $WwwrootDir -Recurse -Force }
    Copy-Item $WebDist -Destination $WwwrootDir -Recurse
}

if (-not (Test-Path (Join-Path $WwwrootDir ".vite\manifest.json"))) {
    throw "server/wwwroot/.vite/manifest.json not found - run without -SkipWeb first"
}

# ---------------------------------------------------------------- server
if (-not $SkipBuild) {
    Write-Host "==> dotnet build -c Release ($ModName)" -ForegroundColor Cyan
    dotnet build -c Release (Join-Path $ServerProjDir "$ModName.csproj")
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)" }
}

if (-not (Test-Path (Join-Path $BuildOut "$ModName.dll"))) {
    throw "Build output not found: $BuildOut\$ModName.dll (build the project first)"
}

# Guard: SPT-provided assemblies must never ship with the mod.
$Leaked = Get-ChildItem $BuildOut -Filter *.dll | Where-Object { $_.Name -ne "$ModName.dll" }
if ($Leaked) {
    throw "Unexpected DLLs in build output (would conflict with SPT): " + (($Leaked | ForEach-Object Name) -join ', ')
}

# ---------------------------------------------------------------- stage + zip
Write-Host "==> Staging to $StageDir" -ForegroundColor Cyan
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

Get-ChildItem $BuildOut -Recurse -File | Where-Object {
    $name = $_.Name
    -not ($ExcludePatterns | Where-Object { $name -like $_ })
} | ForEach-Object {
    $rel  = $_.FullName.Substring($BuildOut.Length).TrimStart('\')
    $dest = Join-Path $StageDir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item $_.FullName -Destination $dest
}

Write-Host "==> Creating zip: $ZipPath" -ForegroundColor Cyan
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path (Join-Path $DistDir "SPT_Runtime") -DestinationPath $ZipPath

Write-Host "==> Packaged files" -ForegroundColor Green
Get-ChildItem -Recurse $StageDir -File | ForEach-Object {
    Write-Host ("  " + $_.FullName.Substring($DistDir.Length + 1))
}

# ---------------------------------------------------------------- deploy
if (-not $SkipDeploy) {
    $SptModsDir = Join-Path $SptRuntimeDir "user\mods"
    if (-not (Test-Path $SptModsDir)) {
        Write-Warning "SPT mods folder not found: $SptModsDir - skipping deploy (use -SptRuntimeDir or -SkipDeploy)"
    } else {
        Write-Host "==> Deploying to $DeployDir" -ForegroundColor Cyan
        if (Test-Path $DeployDir) { Remove-Item $DeployDir -Recurse -Force }
        Copy-Item $StageDir -Destination $DeployDir -Recurse
        Write-Host "Deployed: $DeployDir" -ForegroundColor Green
    }
}

Write-Host "==> Done: $ZipPath" -ForegroundColor Green
