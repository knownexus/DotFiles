# VCS-aware prompt — mirrors zsh 40-prompt
# Left:  hostname$
# Right (same line, right-aligned): path [branch:rev,status]
# Status flags use the same shorthand as the zsh version: ? A D R M

function script:Get-VcsInfo {
    # Returns a hashtable with Branch, Revno, Flags — or $null outside a repo.

    # Git
    $gitRoot = git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $gitRoot) {
        $branch = git symbolic-ref --short HEAD 2>$null
        if (-not $branch) { $branch = git rev-parse --short HEAD 2>$null }
        $revno  = git rev-parse --short=7 HEAD 2>$null

        $flags = ''
        $porcelain = git status --porcelain 2>$null
        if ($porcelain) {
            if ($porcelain -match '^\?\?')          { $flags += '?' }
            if ($porcelain -match '^[A][^A]|^.A')   { $flags += 'A' }
            if ($porcelain -match '^[D]|^.[D]')     { $flags += 'D' }
            if ($porcelain -match '^[R]|^.[R]')     { $flags += 'R' }
            if ($porcelain -match '^[M]|^.[M]')     { $flags += 'M' }
        }

        return @{ VCS = 'git'; Branch = $branch; Revno = $revno; Flags = $flags }
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
    # Update window title on each prompt (mirrors title_precmd)
    Set-WindowTitle "pwsh: $(Get-Location)"

    $esc    = [char]27
    $reset  = "${esc}[0m"
    $cyan   = "${esc}[1;36m"
    $yellow = "${esc}[1;33m"
    $blue   = "${esc}[1;34m"

    $hostName = $env:COMPUTERNAME

    $location = "$(Get-Location)"

    $vcs = Get-VcsInfo
    if ($vcs) {
        $statusSuffix = if ($vcs.Flags) { ",$($vcs.Flags)" } else { '' }
        $branchPart   = "[$($vcs.Branch):$($vcs.Revno)$statusSuffix]"
        $rightColored = "${yellow}${location}${reset} ${cyan}${branchPart}${reset}"
        $rightPlain   = "$location $branchPart"
    } else {
        $rightColored = "${yellow}${location}${reset}"
        $rightPlain   = $location
    }

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

    return "${blue}${hostName}${reset}`$ "
}
