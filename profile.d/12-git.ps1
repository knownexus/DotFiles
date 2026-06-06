# Git aliases — mirrors zsh 12-git
# Note: PowerShell function names cannot contain ! so g! becomes gstat, remote! becomes gitremote-reset.

# Status
function global:gstat {
    $esc    = [char]27
    $reset  = "${esc}[0m"
    $bold   = "${esc}[1m"
    $green  = "${esc}[32m"
    $red    = "${esc}[31m"
    $yellow = "${esc}[33m"
    $cyan   = "${esc}[36m"
    $gray   = "${esc}[90m"

    $branch = git symbolic-ref --short HEAD 2>$null
    if (-not $branch) { $branch = git rev-parse --short HEAD 2>$null }
    $revno  = git rev-parse --short=7 HEAD 2>$null

    $up = [char]0x2191
    $dn = [char]0x2193
    $ahead  = git rev-list --count '@{upstream}..HEAD' 2>$null
    $behind = git rev-list --count 'HEAD..@{upstream}' 2>$null
    $aheadStr  = if ($ahead  -and [int]$ahead.Trim()  -gt 0) { " ${green}${up}$($ahead.Trim())${reset}" }  else { '' }
    $behindStr = if ($behind -and [int]$behind.Trim() -gt 0) { " ${red}${dn}$($behind.Trim())${reset}" } else { '' }

    Write-Host "${bold}branch:${reset} ${cyan}${branch}${reset} ${gray}${revno}${reset}${aheadStr}${behindStr}"
    Write-Host ''

    $lines     = @(git status --porcelain 2>$null)
    $staged    = @($lines | Where-Object { $_ -match '^[AMDRC]' })
    $unstaged  = @($lines | Where-Object { $_ -match '^.[MD]' })
    $conflicts = @($lines | Where-Object { $_ -match '^(UU|AA|DD|AU|UA|DU|UD)' })
    $untracked = @($lines | Where-Object { $_ -match '^\?\?' })

    if ($staged.Count -gt 0) {
        Write-Host "${bold}${green}Staged:${reset}"
        foreach ($l in $staged) {
            $label = switch ($l[0]) { 'A'{'new file'}; 'M'{'modified'}; 'D'{'deleted '}; 'R'{'renamed '}; 'C'{'copied  '}; default{$l[0]} }
            Write-Host "  ${green}${label}:${reset} $($l.Substring(3))"
        }
        Write-Host ''
    }

    if ($unstaged.Count -gt 0) {
        Write-Host "${bold}${red}Unstaged:${reset}"
        foreach ($l in $unstaged) {
            $label = switch ($l[1]) { 'M'{'modified'}; 'D'{'deleted '}; default{$l[1]} }
            Write-Host "  ${red}${label}:${reset} $($l.Substring(3))"
        }
        Write-Host ''
    }

    if ($conflicts.Count -gt 0) {
        Write-Host "${bold}${yellow}Conflicts:${reset}"
        foreach ($l in $conflicts) { Write-Host "  ${yellow}conflict:${reset} $($l.Substring(3))" }
        Write-Host ''
    }

    if ($untracked.Count -gt 0) {
        Write-Host "${bold}${gray}Untracked:${reset}"
        foreach ($l in $untracked) { Write-Host "  ${gray}$($l.Substring(3))${reset}" }
        Write-Host ''
    }

    if ($lines.Count -eq 0) {
        Write-Host "${green}Nothing to commit, working tree clean${reset}"
    }
}
function global:g!   { gstat }
function global:git! { gstat }

# Add
function global:ga     { git add @args }
function global:gita   { git add @args }
function global:gau     { git add -u @args }
function global:gitau   { git add -u @args }

# Whitespace-ignoring staged apply
function global:gaw    { git diff -w | git apply --cached --ignore-whitespace }
function global:gaw1   {
    param([Parameter(Mandatory)][string]$Include)
    git diff -w | git apply --cached --ignore-whitespace "--include=$Include"
}

# Blame
function global:gbl    { git blame @args }

# Commit
# gc is a built-in alias for Get-Content — remove it so our version wins
Remove-Item -Path Alias:gc -Force -ErrorAction SilentlyContinue
function global:git-commit { git commit @args }
Set-Alias -Name gc -Value git-commit -Force -Option AllScope
function global:gitca  { git commit --amend @args }
function global:gca  { git commit --amend @args }
function global:gitcm  { git commit -m @args }
function global:uncommit { git reset --soft HEAD^ }

# Diff
function global:gdf    { git diff @args }
function global:gdfs   { git diff --staged @args }
function global:gdf1   { git diff HEAD~1 @args }

# Checkout / branch
function global:gitb   { git branch @args }
function global:gitc   { git checkout @args }
function global:gitcb  { git checkout -b @args }
function global:gitcd  { git checkout develop }
function global:gres   { git checkout -- @args }
function global:gress  { git restore --staged @args }
function global:gitres { git checkout -- @args }

# Log
# gl is a built-in alias for Get-Location — remove it so our version wins
Remove-Item -Path Alias:gl -Force -ErrorAction SilentlyContinue

$script:_GlFmt = '%C(bold blue)%h%C(reset)  %C(bold green)%<(14)%ar%C(reset)  %C(cyan)%<(20,trunc)%an%C(reset)  %s%C(bold yellow)%d%C(reset)'

$script:_LogColors = @{
    Esc   = [char]27
    Up    = [char]0x2191
    Dn    = [char]0x2193
}

function script:Write-LogHeader {
    $esc   = $script:_LogColors.Esc
    $up    = $script:_LogColors.Up
    $dn    = $script:_LogColors.Dn
    $reset = "${esc}[0m"
    $gray  = "${esc}[90m"
    $cyan  = "${esc}[36m"
    $green = "${esc}[32m"
    $red   = "${esc}[31m"

    $originSha = git rev-parse '@{upstream}' 2>$null
    if ($LASTEXITCODE -eq 0 -and $originSha) {
        $originShort  = git rev-parse --short=7 $originSha.Trim()
        $upstreamName = git rev-parse --abbrev-ref '@{upstream}' 2>$null
        $ahead  = git rev-list --count '@{upstream}..HEAD' 2>$null
        $behind = git rev-list --count 'HEAD..@{upstream}' 2>$null
        $aheadStr  = if ($ahead  -and [int]$ahead.Trim()  -gt 0) { "  ${cyan}${up}$($ahead.Trim()) ahead${reset}" }  else { '' }
        $behindStr = if ($behind -and [int]$behind.Trim() -gt 0) { "  ${red}${dn}$($behind.Trim()) behind${reset}" } else { '' }
        Write-Host "${gray}origin:${reset} ${cyan}${originShort}${reset} ${gray}(${upstreamName})${reset}${aheadStr}${behindStr}"
    }

    $branch   = git symbolic-ref --short HEAD 2>$null
    $headFull = git rev-parse HEAD 2>$null
    foreach ($candidate in @('develop', 'master', 'main', 'origin/develop', 'origin/master', 'origin/main')) {
        if ($candidate -eq $branch -or $candidate -eq "origin/$branch") { continue }
        $mergeSha = git merge-base HEAD $candidate 2>$null
        if ($LASTEXITCODE -eq 0 -and $mergeSha -and $mergeSha.Trim() -ne $headFull.Trim()) {
            $baseShort   = git rev-parse --short=7 $mergeSha.Trim()
            $commitCount = git rev-list --count "$($mergeSha.Trim())..HEAD" 2>$null
            Write-Host "${gray}base:${reset}   ${green}${baseShort}${reset} ${gray}(${candidate})${reset}  ${gray}${commitCount} commit(s) on this branch${reset}"
            break
        }
    }

    Write-Host ''
}

# Pretty log — auto-fits terminal height, no pager
function global:gl {
    # Pre-count header lines so we can compute the limit before printing anything
    $headerLines = 1  # blank line always emitted
    git rev-parse '@{upstream}' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $headerLines++ }
    $branch   = git symbolic-ref --short HEAD 2>$null
    $headFull = git rev-parse HEAD 2>$null
    foreach ($candidate in @('develop', 'master', 'main', 'origin/develop', 'origin/master', 'origin/main')) {
        if ($candidate -eq $branch -or $candidate -eq "origin/$branch") { continue }
        $mergeSha = git merge-base HEAD $candidate 2>$null
        if ($LASTEXITCODE -eq 0 -and $mergeSha -and $mergeSha.Trim() -ne $headFull.Trim()) {
            $headerLines++
            break
        }
    }

    $available = $Host.UI.RawUI.WindowSize.Height - $headerLines - 3  # prompt blank + prompt + buffer
    $limit     = [int][Math]::Max(1, [Math]::Floor($available / 5) - 1)

    script:Write-LogHeader
    git log --color=always "--max-count=$limit" `
        '--format=tformat:%C(yellow)%H%C(reset)%C(auto)%d%C(reset)%n%C(brightblack)%an <%ae>  %ad%C(reset)%n%n%C(249)%w(0,4,4)%B%C(reset)' `
        '--date=format:%a %d/%m/%y %H:%M' `
        @args
}
Set-Alias -Name gitl -Value gl -Force -Option AllScope

# Pretty log — with per-commit file stats
function global:gl2 {
    script:Write-LogHeader
    $esc        = [char]27
    $reset      = "${esc}[0m"
    $gray       = "${esc}[90m"
    $green      = "${esc}[32m"
    $red        = "${esc}[31m"
    $pendingSha = $null
    $buffer     = [System.Collections.Generic.List[string]]::new()
    git log --color=always --shortstat `
        '--format=tformat:%C(yellow)%H%C(reset)%C(auto)%d%C(reset)%n%C(brightblack)%an <%ae>  %ad%C(reset)%n%n%C(249)%w(0,4,4)%B%w(0,0,0)%C(reset)%n>>END<<' `
        '--date=format:%a %d/%m/%y %H:%M' `
        @args |
    ForEach-Object {
        if ($_ -match '^(\e\[\d+m|\e\[\d+;\d+m)*[0-9a-f]{40}') {
            # Flush previous commit if it had no stat
            if ($pendingSha) {
                $pendingSha
                foreach ($line in $buffer) { $line }
                $buffer.Clear()
            }
            $pendingSha = $_
        } elseif ($_ -match '>>END<<') {
            # Just consume it — stat arrives after the following blank line
        } elseif ($_ -match '^\s*(\d+) files? changed(?:, (\d+) insertions?\(\+\))?(?:, (\d+) deletions?\(-\))?') {
            $f = $Matches[1]
            $i = if ($Matches[2]) { $Matches[2] } else { '0' }
            $d = if ($Matches[3]) { $Matches[3] } else { '0' }
            $stat = "  ${gray}${f}f ${green}+${i}${reset} ${red}-${d}${reset}"
            "$pendingSha$stat"
            $pendingSha = $null
            foreach ($line in $buffer) { $line }
            $buffer.Clear()
        } else {
            if ($pendingSha) {
                $buffer.Add($_)
            } else {
                $_
            }
        }
    } | less -RX
}
Set-Alias -Name gitl2 -Value gl2 -Force -Option AllScope

# Graph log — tree on the left, compact detail on the right, scrollable
function global:gitlg {
    git log --graph --abbrev-commit --decorate --all --color=always `
        "--format=format:%C(bold blue)%h%C(reset)  %C(bold green)%<(14)%ar%C(reset)  %C(cyan)%<(20,trunc)%an%C(reset)  %s%C(bold yellow)%d%C(reset)" `
        @args | less -RX
}

# Ultra-compact graph (hash + subject + refs only)
function global:gitlg2 {
    git log --oneline --graph --decorate --all --color=always @args
}

# Show
function global:gs     { git show @args }
function global:gits   { git show @args }
function global:gitsn  { git show --name-only @args }

# Stash
Remove-Item -Path Alias:gp -Force -ErrorAction SilentlyContinue
function global:git-stash-pop { git stash pop }
Set-Alias -Name gp -Value git-stash-pop -Force -Option AllScope
function global:gst    { git stash push @args }
function global:gstl   { git stash list @args }
function global:stash  { git stash save @args }
function global:stashp { git stash pop }

# Fetch / pull
function global:gitf   { git fetch @args }
function global:gitpl  { git pull --rebase @args }

# Cherry-pick
function global:gitcp  { git cherry-pick @args }

# Clean working tree (untracked files and dirs)
function global:gclean { git clean -fd @args }

# Push
function global:gpf    { git push --force @args }
function global:gitfp  { git push --force @args }
function global:gitpf  { git push --force @args }

# Push current branch upstream (mirrors gp1)
function global:gp1 {
    $branch = git rev-parse --abbrev-ref HEAD
    git push --set-upstream origin $branch
}

# Push local branch to a named remote branch (mirrors pushdr)
function global:pushdr {
    param([Parameter(Mandatory)][string]$RemoteBranch)
    $local = git rev-parse --abbrev-ref HEAD
    git push -u origin "${local}:${RemoteBranch}"
}

# Remote
function global:gru    { git remote update }
function global:grpo   { git remote prune origin }
function global:giturl { git config --get remote.origin.url }

# Hard-reset to remote HEAD (mirrors remote!)
function global:gitremote-reset {
    $branch = git rev-parse --abbrev-ref HEAD
    git remote update
    git reset --hard "origin/$branch"
}
function global:remote! {
    $branch = git rev-parse --abbrev-ref HEAD
    git remote update
    git reset --hard "origin/$branch"
}

function global:grh    { git reset --hard @args }


# Rebase helpers
function global:grd    { git rebase develop @args }
function global:grm    { git rebase master @args }
function global:gro    { git rebase origin @args }
function global:groi   { git rebase origin -i @args }
function global:grod   { git rebase origin/develop @args }
function global:gitra  { git rebase --abort }
function global:gitrc  { git rebase --continue }
function global:gitra2 { git rebase --abort }
function global:groot  { git rebase -i --root master }

function global:gri {
    param([string]$Target = '1')
    if ($Target -match '^\d+$') {
        git rebase -i "HEAD~$Target"
    } else {
        # SHA / branch / tag / HEAD~N — rebase from its parent so the named
        # commit itself is the first entry in the todo list (editable).
        git rebase -i "$Target^"
    }
}

function global:grim {
    param([string]$Target = '1')
    if ($Target -match '^\d+$') {
        git rebase -i --rebase-merges "HEAD~$Target"
    } else {
        git rebase -i --rebase-merges "$Target^"
    }
}

# Rebase onto a branch found by partial name (mirrors rebase alias)
function global:Invoke-RebaseOnto {
    param([Parameter(Mandatory)][string]$BranchFragment)
    $branch = git reflog show --all 2>$null |
        Where-Object { $_ -match $BranchFragment -and $_ -match 'refs/' } |
        Select-Object -First 1 |
        ForEach-Object {
            ($_ -split '\s+')[1] -replace '^refs/remotes/', '' -replace '@\{.*', ''
        }
    if ($branch) { git rebase $branch }
    else { Write-Error "No branch matching '$BranchFragment' found in reflog" }
}
Set-Alias -Name rebaseon -Value Invoke-RebaseOnto

# Reset
function global:gup    { git reset HEAD~1 }

# Cached rm
function global:gitrmc { git rm --cached @args }

# Assume-unchanged
function global:ignoreme     { git update-index --assume-unchanged @args }
function global:dontignoreme { git update-index --no-assume-unchanged @args }

# Update shortcuts
function global:update   { git remote update; git rebase origin }
function global:updated  { git remote update; git rebase origin/develop }

# Clear screen + status
function global:rs { Clear-Host; git status }

# Returns C:\repos\DriveFurtherAPI from any worktree within that project.
# The main worktree is always first in the list; its parent is the project root.
function script:Get-WorktreeProjectRoot {
    $main = git worktree list --porcelain |
        Where-Object { $_ -match '^worktree ' } |
        Select-Object -First 1 |
        ForEach-Object { $_ -replace '^worktree ', '' }
    return Split-Path $main -Parent
}

# Returns all worktree paths for the current repo
function script:Get-WorktreePaths {
    git worktree list --porcelain |
        Where-Object { $_ -match '^worktree ' } |
        ForEach-Object { $_ -replace '^worktree ', '' }
}

function global:wt-go {
    param([Parameter(Mandatory)][string]$Name)
    $match = Get-WorktreePaths |
        Where-Object { $_ -like "*$Name*" } |
        Select-Object -First 1
    if ($match) { Set-Location $match } else { Write-Host "No worktree matching '$Name'" }
}


function global:wt-list { git worktree list }

# Create a feature worktree at <project-root>\feature\<TicketId>-<Desc>
function global:wt-feature {
    param(
        [Parameter(Mandatory)][string]$TicketId,
        [Parameter(Mandatory)][string]$Desc,
        [string]$Base = 'develop'
    )
    $root   = Get-WorktreeProjectRoot
    $wtPath = Join-Path $root "feature\$TicketId-$Desc"
    git worktree add $wtPath -b "feature/$TicketId-$Desc" $Base
}

# Create a fix worktree at <project-root>\fix\<TicketId>-<Desc>
function global:wt-fix {
    param(
        [Parameter(Mandatory)][string]$TicketId,
        [Parameter(Mandatory)][string]$Desc,
        [string]$Base = 'develop'
    )
    $root   = Get-WorktreeProjectRoot
    $wtPath = Join-Path $root "fix\$TicketId-$Desc"
    git worktree add $wtPath -b "fix/$TicketId-$Desc" $Base
}

# Checkout an existing branch into a worktree, preserving path structure.
# e.g. wt-c feature/ABC-123-foo  →  <project-root>\feature\ABC-123-foo
function global:wt-c {
    param([Parameter(Mandatory)][string]$Branch)
    $root   = Get-WorktreeProjectRoot
    $wtPath = Join-Path $root ($Branch -replace '/', '\')
    git worktree add $wtPath $Branch
}

# Remove a worktree and delete its local branch
function global:wt-done {
    param([Parameter(Mandatory)][string]$Name)
    $match = Get-WorktreePaths |
        Where-Object { $_ -like "*$Name*" } |
        Select-Object -First 1
    if (-not $match) { Write-Host "No worktree matching '$Name'"; return }
    $branch = git -C $match branch --show-current
    git worktree remove $match
    if ($branch) { git branch -d $branch }
}

function global:wt-done-f {
    param([Parameter(Mandatory)][string]$Name)
    $match = Get-WorktreePaths |
        Where-Object { $_ -like "*$Name*" } |
        Select-Object -First 1
    if (-not $match) { Write-Host "No worktree matching '$Name'"; return }
    $branch = git -C $match branch --show-current
    git worktree remove --force $match
    if ($branch) { git branch -D $branch }
}

# Show git status for every worktree in the current repo
function global:wt! {
    Get-WorktreePaths | ForEach-Object {
        Write-Host "`n=== $_ ===" -ForegroundColor Cyan
        git -C $_ status --short
    }
}

# Prune stale worktree references
function global:wt-prune { git worktree prune -v }

