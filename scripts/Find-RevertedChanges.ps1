#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Find changes made in commit A that were undone in commit B.

.DESCRIPTION
    Compares the diffs of two commits and reports:
      - Lines that commit A added, which commit B then removed
      - Lines that commit A removed, which commit B then re-added

.PARAMETER CommitA
    The first commit (the one that made the original changes).

.PARAMETER CommitB
    The second commit (the one suspected of undoing those changes).

.EXAMPLE
    .\Find-RevertedChanges.ps1 abc1234 def5678

.EXAMPLE
    .\Find-RevertedChanges.ps1 HEAD~5 HEAD~2
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$CommitA,

    [Parameter(Mandatory, Position = 1)]
    [string]$CommitB
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DiffLines {
    param(
        [string]$Commit,
        [char]$Sign
    )

    $opposite = if ($Sign -eq '+') { '+' } else { '-' }

    git show $Commit |
        Where-Object {
            $_.Length -gt 1 -and
            $_[0] -eq $Sign -and
            $_[1] -ne $opposite  # skip +++ / --- file header lines
        } |
        ForEach-Object { $_.Substring(1) } |
        Where-Object { $_.Trim() -ne '' } |
        Sort-Object -Unique
}

# Resolve to full SHAs so output is unambiguous
$shaA = git rev-parse --short $CommitA 2>$null
$shaB = git rev-parse --short $CommitB 2>$null

if (-not $shaA) { Write-Error "Could not resolve commit: $CommitA"; exit 1 }
if (-not $shaB) { Write-Error "Could not resolve commit: $CommitB"; exit 1 }

Write-Host ""
Write-Host "Comparing:" -ForegroundColor DarkGray
Write-Host "  A = $shaA  ($CommitA)" -ForegroundColor DarkGray
Write-Host "  B = $shaB  ($CommitB)" -ForegroundColor DarkGray
Write-Host ""

$aAdded   = @(Get-DiffLines -Commit $CommitA -Sign '+')
$aRemoved = @(Get-DiffLines -Commit $CommitA -Sign '-')
$bAdded   = @(Get-DiffLines -Commit $CommitB -Sign '+')
$bRemoved = @(Get-DiffLines -Commit $CommitB -Sign '-')

# Lines A added that B then removed (A's addition was reverted)
$reverted = $aAdded | Where-Object { $bRemoved -contains $_ }

# Lines A removed that B then re-added (A's deletion was undone)
$readded  = $aRemoved | Where-Object { $bAdded -contains $_ }

$found = $false

if ($reverted) {
    $found = $true
    Write-Host "A added these lines — B then removed them:" -ForegroundColor Yellow
    $reverted | ForEach-Object {
        Write-Host "  - $_" -ForegroundColor Red
    }
    Write-Host ""
}

if ($readded) {
    $found = $true
    Write-Host "A removed these lines — B then re-added them:" -ForegroundColor Yellow
    $readded | ForEach-Object {
        Write-Host "  + $_" -ForegroundColor Green
    }
    Write-Host ""
}

if (-not $found) {
    Write-Host "No changes from $shaA appear to be undone in $shaB." -ForegroundColor Cyan
}
