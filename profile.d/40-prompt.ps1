# VCS-aware prompt — mirrors zsh 40-prompt
# Respects $global:RepoRoot / $global:RepoSymbol (set in 70-variables.ps1).
# Left:  hostname$
# Right (same line, right-aligned): path [branch:rev,status]
# Status flags use the same shorthand as the zsh version: ? A D R M

# Truncate a path to its last 3 segments (mirrors zsh's %(4~|.../%3~|%~)) —
# only applied outside a git repo, same as the zsh side.
function script:Get-TruncatedPath {
    param([string]$Path)
    $segments = $Path -split '\\' | Where-Object { $_ }
    if ($segments.Count -gt 3) {
        return '...\' + ($segments[($segments.Count - 3)..($segments.Count - 1)] -join '\')
    }
    return $Path
}

# Desktop notification when a command crosses $global:ReportTime (set in
# 30-settings.ps1) — mirrors the zsh notify-send hook. Uses Get-History's
# own Start/EndExecutionTime rather than a preexec/precmd pair, since
# PowerShell doesn't have a direct preexec equivalent. No-ops if BurntToast
# isn't installed.
$global:LastNotifiedHistoryId = 0
function script:Invoke-LongCommandNotify {
    $last = Get-History -Count 1 -ErrorAction SilentlyContinue
    if (-not $last -or $last.Id -eq $global:LastNotifiedHistoryId) { return }
    $global:LastNotifiedHistoryId = $last.Id

    $duration = ($last.EndExecutionTime - $last.StartExecutionTime).TotalSeconds
    $threshold = if ($global:ReportTime) { $global:ReportTime } else { 10 }
    if ($duration -lt $threshold) { return }

    if (-not (Get-Module -ListAvailable -Name BurntToast)) { return }
    Import-Module BurntToast -ErrorAction SilentlyContinue
    try {
        New-BurntToastNotification -Text "Command finished ($([math]::Round($duration))s)", $last.CommandLine
    } catch {
        # Never let a notification failure break the prompt
    }
}

# Replace $global:RepoRoot prefix with $global:RepoSymbol in a path string.
# e.g. C:\repos\DriveFurtherAPI\develop  →  #\DriveFurtherAPI\develop
function script:Get-ShortPath {
    param([string]$Path)
    if ($global:RepoRoot -and $global:RepoSymbol) {
        $root = $global:RepoRoot.TrimEnd('\')
        if ($Path -ieq $root) {
            return $global:RepoSymbol
        }
        if ($Path.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $global:RepoSymbol + $Path.Substring($root.Length)
        }
    }
    return $Path
}

function script:Get-VcsInfo {
    # Returns a hashtable with Branch, Revno, Flags — or $null outside a repo.

    # Git
    $gitRoot = git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $gitRoot) {
        $branch = git symbolic-ref --short HEAD 2>$null
        if (-not $branch) { $branch = git rev-parse --short HEAD 2>$null }
        $revno  = git rev-parse --short=7 HEAD 2>$null

        # A worktree has a .git FILE; the main checkout has a .git DIRECTORY.
        $gitDotGit  = Join-Path $gitRoot '.git'
        $isWorktree = Test-Path $gitDotGit -PathType Leaf

        $flags = ''
        $porcelain = git status --porcelain 2>$null
        if ($porcelain) {
            if ($porcelain -match '^\?\?')          { $flags += '?' }
            if ($porcelain -match '^[A][^A]|^.A')   { $flags += 'A' }
            if ($porcelain -match '^[D]|^.[D]')     { $flags += 'D' }
            if ($porcelain -match '^[R]|^.[R]')     { $flags += 'R' }
            if ($porcelain -match '^[M]|^.[M]')     { $flags += 'M' }
        }

        return @{ VCS = 'git'; Branch = $branch; Revno = $revno; Flags = $flags; IsWorktree = $isWorktree }
    }

    # SVN
    if ((Get-Command svn -ErrorAction SilentlyContinue) -and (Test-Path '.svn')) {
        $branch = (svn info 2>$null | Select-String 'URL:') -replace '^.*/', ''
        $revno  = (svn info 2>$null | Select-String 'Revision:') -replace 'Revision:\s*', ''
        return @{ VCS = 'svn'; Branch = $branch; Revno = $revno; Flags = '' }
    }

    return $null
}

function global:prompt {
    # Captured before anything else runs — everything below (Set-WindowTitle,
    # git status, etc.) would otherwise overwrite $?/$LASTEXITCODE long
    # before the exit-code indicator gets built, silently breaking it no
    # matter what the user's last command actually returned. (Same class of
    # bug found and fixed on the zsh side of this config.)
    $lastSucceeded = $?
    $lastExitCode  = $LASTEXITCODE

    # Update window title on each prompt (mirrors title_precmd)
    Set-WindowTitle "pwsh: $(Get-Location)"

    Invoke-LongCommandNotify

    $esc    = [char]27
    $reset  = "${esc}[0m"
    $cyan   = "${esc}[1;36m"
    $yellow = "${esc}[1;33m"
    $blue   = "${esc}[1;34m"
    $red    = "${esc}[1;31m"

    $hostName = $env:COMPUTERNAME

    $location = Get-ShortPath "$(Get-Location)"

    $vcs = Get-VcsInfo
    if ($vcs) {
        $statusSuffix = if ($vcs.Flags) { ",$($vcs.Flags)" } else { '' }
        $branchLabel  = if ($vcs.IsWorktree) { "wt:$($vcs.Branch)" } else { $vcs.Branch }
        $branchPart   = "[${branchLabel}:$($vcs.Revno)$statusSuffix]"
        $rightColored = "${yellow}${location}${reset} ${cyan}${branchPart}${reset}"
        $rightPlain   = "$location $branchPart"
    } else {
        $location     = Get-TruncatedPath $location
        $rightColored = "${yellow}${location}${reset}"
        $rightPlain   = $location
    }

    # Background job count and exit-code indicator, shown just before the $
    $jobCount  = (Get-Job -State Running -ErrorAction SilentlyContinue | Measure-Object).Count
    $jobPart   = if ($jobCount -gt 0) { "[$jobCount] " } else { '' }
    $failed    = (-not $lastSucceeded) -or ($null -ne $lastExitCode -and $lastExitCode -ne 0)
    $errorPart = if ($failed) { "${red}✗${reset} " } else { '' }

    # Blank line above the prompt for breathing room
    Write-Host ""

    # Right-align the path + VCS info on the same line as the prompt.
    # Strategy: move cursor to (terminal width - visible length + 1), write the
    # coloured right side, then return to column 1 so the prompt renders at the
    # left edge.
    $width = $Host.UI.RawUI.WindowSize.Width
    $col   = $width - $rightPlain.Length + 1
    if ($col -lt 1) { $col = 1 }

    Write-Host "${esc}[${col}G${rightColored}${esc}[1G" -NoNewline

    return "${jobPart}${errorPart}${blue}`$ ${reset}"
}
