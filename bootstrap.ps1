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
#   2. Installs everything this config uses via winget, so nothing has to be
#      installed by hand afterward: git, yq (needed at runtime by
#      `gitaliases`/`cheat` to read shared/commands.yaml — same binary and
#      query language the zsh side uses), ripgrep, fzf, bat, glow, zoxide,
#      direnv
#   3. Adds Git for Windows' usr\bin (vim, less, grep, sed, tar, ...) to the
#      user PATH — that one install covers the Unix tools this config's
#      aliases assume, instead of chasing down separate winget packages for
#      each of them
#   4. Deploys the shared vim config (~/.vimrc from shared/vim/vimrc, plus the
#      cheatsheet <space>? opens) so vim behaves the same here as on Linux
#   5. Installs the optional PowerShell modules (PSFzf, BurntToast)
#   6. Reports what's available at the end
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

# --- 2. winget-installed tooling ----------------------------------------------
$hasWinget = [bool](Get-Command winget -ErrorAction SilentlyContinue)
if (-not $hasWinget) {
    Write-Host "==> winget not found -- install the tools below yourself, then re-run"
}

function Install-Tool {
    param([string]$Command, [string]$WingetId)
    if (Get-Command $Command -ErrorAction SilentlyContinue) {
        Write-Host "    ${Command}: found"
        return
    }
    Write-Host "==> $Command not found -- installing ($WingetId)"
    if ($hasWinget) {
        winget install --id $WingetId -e --source winget --accept-source-agreements --accept-package-agreements
    } else {
        Write-Host "    winget not found -- install $Command yourself: $WingetId"
    }
}

Install-Tool git   Git.Git
Install-Tool yq    MikeFarah.yq
Install-Tool rg    BurntSushi.ripgrep.MSVC
Install-Tool fzf   junegunn.fzf
Install-Tool bat   sharkdp.bat
Install-Tool glow  charmbracelet.glow
Install-Tool zoxide ajeetdsouza.zoxide
Install-Tool direnv direnv.direnv

# --- 3. Git for Windows' usr\bin on PATH --------------------------------------
# Bundles vim, less, grep, sed, tar, etc. -- covers the Unix tools this
# config's aliases (v/vb, git log | less, ...) assume, in one install
# instead of chasing down a separate winget package for each.
#
# Resolved from the actual git.exe on PATH rather than hardcoded, since the
# install root varies: machine-wide winget/system installs land in
# C:\Program Files\Git, but a per-user install (also common, e.g. under
# %LOCALAPPDATA%\Programs\Git) does not -- a hardcoded path silently misses
# that case (Test-Path just says "not found" and moves on).
$candidateRoots = @()
$gitCmd = Get-Command git -ErrorAction SilentlyContinue
if ($gitCmd) {
    $gitBinDir = Split-Path $gitCmd.Source -Parent   # ...\cmd or ...\mingw64\bin
    $candidateRoots += Split-Path $gitBinDir -Parent  # strips \cmd or \bin
    if ((Split-Path $candidateRoots[-1] -Leaf) -eq 'mingw64') {
        $candidateRoots += Split-Path $candidateRoots[-1] -Parent  # strips \mingw64 too
    }
}
$candidateRoots += 'C:\Program Files\Git'
$candidateRoots += "$env:LOCALAPPDATA\Programs\Git"

$gitUsrBin = $candidateRoots |
    ForEach-Object { Join-Path $_ 'usr\bin' } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1

if ($gitUsrBin) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $gitUsrBin) {
        [Environment]::SetEnvironmentVariable('Path', "$userPath;$gitUsrBin", 'User')
        $env:Path += ";$gitUsrBin"   # so this session's final report below sees it too
        Write-Host "    Added $gitUsrBin to user PATH (vim, less, grep, sed, ...)"
    } else {
        Write-Host "    $gitUsrBin already on user PATH"
    }
} else {
    Write-Host "    Couldn't find Git for Windows' usr\bin -- install Git for Windows first"
}

# --- 4. vim config -------------------------------------------------------------
# ~/.vimrc is written as a one-line stub that sources the repo copy, rather
# than a copy of it. A copy goes stale the moment shared/vim/vimrc is edited
# and you then spend ten minutes wondering why a new mapping "doesn't work";
# the stub means the repo file IS the live config, matching the symlink
# bootstrap.sh makes on Linux. (A real symlink would need Developer Mode or
# an elevated shell, which this script deliberately does not require.)
$vimSrc  = Join-Path $repoDir 'shared\vim\vimrc'
$vimDest = Join-Path $HOME '.vimrc'
# vim wants forward slashes in a :source path.
$stub    = '" Managed by dotfiles bootstrap - edit shared/vim/vimrc, not this file.' + "`n" +
           'source ' + ($vimSrc -replace '\\', '/') + "`n"

Write-Host "==> Pointing ~/.vimrc at $vimSrc"
if ((Test-Path $vimDest) -and ((Get-Content $vimDest -Raw) -ne $stub)) {
    $existing = Get-Content $vimDest -Raw
    if ($existing -notmatch [regex]::Escape('shared/vim/vimrc')) {
        Copy-Item $vimDest "$vimDest.bak.$(Get-Date -Format yyyyMMddHHmmss)"
        Write-Host "    Backed up the existing ~/.vimrc"
    }
}
# LF, no BOM - this file is sourced by vim, which is unforgiving about both.
[System.IO.File]::WriteAllText($vimDest, $stub.Replace("`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))
New-Item -ItemType Directory -Force -Path (Join-Path $HOME '.vim\undodir') | Out-Null
Write-Host "    ~/.vimrc sources the repo; edits to shared/vim/vimrc are live in the next vim"

# --- 5. Optional PowerShell modules --------------------------------------------
foreach ($mod in @('PSFzf', 'BurntToast')) {
    if (Get-Module -ListAvailable -Name $mod) {
        Write-Host "    $mod (module): found"
    } else {
        Write-Host "==> $mod module not found -- installing"
        Install-Module $mod -Scope CurrentUser -Force -AllowClobber
    }
}

# --- 6. Final report -------------------------------------------------------
Write-Host ""
Write-Host "==> Tooling status:"
foreach ($cmd in @('git', 'yq', 'rg', 'fzf', 'bat', 'glow', 'zoxide', 'direnv', 'vim', 'less')) {
    $found = Get-Command $cmd -ErrorAction SilentlyContinue
    Write-Host "    [$(if ($found) {'x'} else {' '})] $cmd"
}
foreach ($mod in @('PSFzf', 'PSReadLine', 'BurntToast')) {
    $found = Get-Module -ListAvailable -Name $mod
    Write-Host "    [$(if ($found) {'x'} else {' '})] $mod (module)"
}

Write-Host ""
Write-Host "==> Done. Open a new pwsh session to load everything."
