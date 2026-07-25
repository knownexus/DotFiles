# Git log — gl, gl2, gitlg, gitlg2

# gl is a built-in alias for Get-Location — remove it so our version wins
Remove-Item -Path Alias:gl -Force -ErrorAction SilentlyContinue

function global:Write-GitLogHeader {
    $esc   = [char]27
    $up    = [char]0x2191
    $dn    = [char]0x2193
    $reset = "${esc}[0m"
    $gray  = "${esc}[90m"
    $cyan  = "${esc}[36m"
    $green = "${esc}[32m"
    $red   = "${esc}[31m"

    $statusLines = git status --porcelain=v2 --branch 2>$null
    $branch   = [string](($statusLines | Where-Object { $_ -match '^# branch\.head ' })     -replace '^# branch\.head ', '')
    $headFull = [string](($statusLines | Where-Object { $_ -match '^# branch\.oid ' })      -replace '^# branch\.oid ', '')
    $upstream = [string](($statusLines | Where-Object { $_ -match '^# branch\.upstream ' }) -replace '^# branch\.upstream ', '')
    $ab       = [string](($statusLines | Where-Object { $_ -match '^# branch\.ab ' })       -replace '^# branch\.ab ', '')

    if ($upstream) {
        $originShort = git rev-parse --short=7 '@{upstream}' 2>$null
        $ahead  = if ($ab -match '\+(\d+)') { $Matches[1] } else { '0' }
        $behind = if ($ab -match '-(\d+)') { $Matches[1] } else { '0' }
        $aheadStr  = if ([int]$ahead  -gt 0) { "  ${cyan}${up}${ahead} ahead${reset}" }  else { '' }
        $behindStr = if ([int]$behind -gt 0) { "  ${red}${dn}${behind} behind${reset}" } else { '' }
        Write-Host "${gray}origin:${reset} ${cyan}${originShort}${reset} ${gray}(${upstream})${reset}${aheadStr}${behindStr}"
    }

    foreach ($candidate in @('develop', 'master', 'main', 'origin/develop', 'origin/master', 'origin/main')) {
        if ($candidate -eq $branch -or $candidate -eq "origin/$branch") { continue }
        $mergeSha = [string](git merge-base HEAD $candidate 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and $mergeSha -and $mergeSha.Trim() -ne $headFull.Trim()) {
            $baseShort   = git rev-parse --short=7 $mergeSha.Trim()
            $commitCount = git rev-list --count "$($mergeSha.Trim())..HEAD" 2>$null
            Write-Host "${gray}base:${reset}   ${green}${baseShort}${reset} ${gray}(${candidate})${reset}  ${gray}${commitCount} commit(s) on this branch${reset}"
            break
        }
    }

    Write-Host ''
}

# Pretty log — scrollable via less
function global:gl {
    if (-not (Assert-GitRepo)) { return }
    Write-GitLogHeader
    try {
        git log --color=always `
            '--format=tformat:%C(yellow)%H%C(reset)%C(auto)%d%C(reset)%n%C(brightblack)%an <%ae>  %ad%C(reset)%n%n%C(249)%w(0,4,4)%B%C(reset)' `
            '--date=format:%a %d/%m/%y %H:%M' `
            @args | less -RX
    } catch {
        if ($_.Exception.Message -notmatch 'pipe') { throw }
    }
}
Set-Alias -Name gitl -Value gl -Force -Option AllScope

# Pretty log — with per-commit file stats
function global:gl2 {
    if (-not (Assert-GitRepo)) { return }
    Write-GitLogHeader
    $esc        = [char]27
    $reset      = "${esc}[0m"
    $gray       = "${esc}[90m"
    $green      = "${esc}[32m"
    $red        = "${esc}[31m"
    $pendingSha = $null
    $buffer     = [System.Collections.Generic.List[string]]::new()
    try {
        git log --color=always --shortstat `
            '--format=tformat:%C(yellow)%H%C(reset)%C(auto)%d%C(reset)%n%C(brightblack)%an <%ae>  %ad%C(reset)%n%n%C(249)%w(0,4,4)%B%w(0,0,0)%C(reset)%n>>END<<' `
            '--date=format:%a %d/%m/%y %H:%M' `
            @args |
        ForEach-Object {
            if ($_ -match '^(\e\[\d+m|\e\[\d+;\d+m)*[0-9a-f]{40}') {
                if ($pendingSha) {
                    $pendingSha
                    foreach ($line in $buffer) { $line }
                    $buffer.Clear()
                }
                $pendingSha = $_
            } elseif ($_ -match '>>END<<') {
                # consumed — stat arrives after the following blank line
            } elseif ($_ -match '^\s*(\d+) files? changed(?:, (\d+) insertions?\(\+\))?(?:, (\d+) deletions?\(-\))?') {
                $f    = $Matches[1]
                $i    = if ($Matches[2]) { $Matches[2] } else { '0' }
                $d    = if ($Matches[3]) { $Matches[3] } else { '0' }
                $stat = "  ${gray}${f}f ${green}+${i}${reset} ${red}-${d}${reset}"
                "$pendingSha$stat"
                $pendingSha = $null
                foreach ($line in $buffer) { $line }
                $buffer.Clear()
            } else {
                if ($pendingSha) { $buffer.Add($_) } else { $_ }
            }
        } | less -RX
    } catch {
        if ($_.Exception.Message -notmatch 'pipe') { throw }
    }
}
Set-Alias -Name gitl2 -Value gl2 -Force -Option AllScope

# Graph log — tree on the left, compact detail on the right
function global:gitlg {
    if (-not (Assert-GitRepo)) { return }
    try {
        git log --graph --abbrev-commit --decorate --all --color=always `
            "--format=format:%C(bold blue)%h%C(reset)  %C(bold green)%<(14)%ar%C(reset)  %C(cyan)%<(20,trunc)%an%C(reset)  %s%C(bold yellow)%d%C(reset)" `
            @args | less -RX
    } catch {
        if ($_.Exception.Message -notmatch 'pipe') { throw }
    }
}

# Ultra-compact graph (hash + subject + refs only)
function global:gitlg2 {
    git log --oneline --graph --decorate --all --color=always @args
}
