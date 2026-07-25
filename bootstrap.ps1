#Requires -Version 5.1
# Deploys the pwsh side of this dotfiles monorepo on Windows.
#
# NOTE: written and reviewed, but never actually run — this environment has
# no pwsh/dotnet available to test against. Read through it before trusting
# it against a real $PROFILE.
#
# What it does:
#   1. Points $PROFILE at this repo's pwsh/Profile.ps1 (backs up any existing
#      profile content first instead of overwriting it)
#   2. Installs yq if missing (needed at runtime by `gitaliases` to read
#      shared/commands.yaml — the same binary and query language the zsh
#      side already uses, just via winget instead of dnf/apt/brew)
#   3. Reports which optional integrations (PSFzf, zoxide, direnv, BurntToast)
#      are already available
#
# Safe to re-run.

$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$profileTarget = Join-Path $repoDir 'pwsh\Profile.ps1'
$sourceLine = ". `"$profileTarget`""

Write-Host "==> Deploying pwsh config from $profileTarget"

# --- 1. $PROFILE wiring ------------------------------------------------------
$profileDir = Split-Path -Parent $PROFILE
if (-not (Test-Path $profileDir)) {
    New-Item -ItemType Directory -Path $profileDir -Force | Out-Null
}

if ((Test-Path $PROFILE) -and (Get-Content $PROFILE -Raw) -match [regex]::Escape($sourceLine)) {
    Write-Host "    `$PROFILE already sources this repo's Profile.ps1 -- skipping"
} else {
    if (Test-Path $PROFILE) {
        $backup = "$PROFILE.bak.$([guid]::NewGuid().ToString('N').Substring(0,8))"
        Copy-Item $PROFILE $backup
        Write-Host "    Existing `$PROFILE backed up to $backup"
    }
    Add-Content -Path $PROFILE -Value "`n$sourceLine"
    Write-Host "    Added Profile.ps1 sourcing line to `$PROFILE"
}

# --- 2. yq ---------------------------------------------------------------
if (-not (Get-Command yq -ErrorAction SilentlyContinue)) {
    Write-Host "==> yq not found (required -- gitaliases/cheat read shared/commands.yaml through it)"
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install --id MikeFarah.yq -e --source winget
    } else {
        Write-Host "    winget not found -- install yq yourself: https://github.com/mikefarah/yq"
    }
} else {
    Write-Host "    yq: found"
}

# --- 3. Optional integrations report -----------------------------------------
Write-Host ""
Write-Host "==> Optional integrations (inert until installed, activate automatically):"
foreach ($mod in @('PSFzf', 'PSReadLine')) {
    $found = Get-Module -ListAvailable -Name $mod
    Write-Host "    [$(if ($found) {'x'} else {' '})] $mod (module)"
}
foreach ($cmd in @('zoxide', 'direnv')) {
    $found = Get-Command $cmd -ErrorAction SilentlyContinue
    Write-Host "    [$(if ($found) {'x'} else {' '})] $cmd"
}
$burntToast = Get-Module -ListAvailable -Name BurntToast
Write-Host "    [$(if ($burntToast) {'x'} else {' '})] BurntToast (module, desktop notifications)"

Write-Host ""
Write-Host "==> Done. Open a new pwsh session to load everything."
