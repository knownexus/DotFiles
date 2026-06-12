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

# Archive
function global:gar {
    param(
        [Parameter(Mandatory = $false)]
        [string]$RepoName
    )

    if (-not (git rev-parse --is-inside-work-tree 2>$null)) {
        Write-Error "Not inside a git repository."
        return
    }

    if (-not $RepoName) {
        $RepoName = Split-Path -Leaf (git rev-parse --show-toplevel)
    }


    $RepoName = ($RepoName -replace '\s+', '_')   # <-- add this


    $hash = git rev-parse --short HEAD
    $out  = "$RepoName-$hash.zip"

    git archive --format=zip HEAD -o $out
    Write-Host "Created $out ($([math]::Round((Get-Item $out).Length / 1MB, 1)) MB)"
}


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

# Find changes made in commit A that were undone in commit B.
# Usage: grev <commit-a> <commit-b>
function global:grev {
    param(
        [Parameter(Mandatory, Position = 0)][string]$CommitA,
        [Parameter(Mandatory, Position = 1)][string]$CommitB
    )

    function _GetDiffLines {
        param([string]$Commit, [char]$Sign)
        git show $Commit |
            Where-Object { $_.Length -gt 1 -and $_[0] -eq $Sign -and $_[1] -ne $Sign } |
            ForEach-Object { $_.Substring(1) } |
            Where-Object { $_.Trim() -ne '' } |
            Sort-Object -Unique
    }

    $shaA = git rev-parse --short $CommitA 2>$null
    $shaB = git rev-parse --short $CommitB 2>$null
    if (-not $shaA) { Write-Error "Could not resolve commit: $CommitA"; return }
    if (-not $shaB) { Write-Error "Could not resolve commit: $CommitB"; return }

    Write-Host "`nA = $shaA  ($CommitA)" -ForegroundColor DarkGray
    Write-Host "B = $shaB  ($CommitB)`n" -ForegroundColor DarkGray

    $aAdded   = @(_GetDiffLines -Commit $CommitA -Sign '+')
    $aRemoved = @(_GetDiffLines -Commit $CommitA -Sign '-')
    $bAdded   = @(_GetDiffLines -Commit $CommitB -Sign '+')
    $bRemoved = @(_GetDiffLines -Commit $CommitB -Sign '-')

    $reverted = $aAdded   | Where-Object { $bRemoved -contains $_ }
    $readded  = $aRemoved | Where-Object { $bAdded   -contains $_ }
    $found    = $false

    if ($reverted) {
        $found = $true
        Write-Host "A added these lines — B then removed them:" -ForegroundColor Yellow
        $reverted | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        Write-Host ""
    }

    if ($readded) {
        $found = $true
        Write-Host "A removed these lines — B then re-added them:" -ForegroundColor Yellow
        $readded | ForEach-Object { Write-Host "  + $_" -ForegroundColor Green }
        Write-Host ""
    }

    if (-not $found) {
        Write-Host "No changes from $shaA appear to be undone in $shaB." -ForegroundColor Cyan
    }
}

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

    # Single subprocess for branch, upstream, ahead/behind, and HEAD sha
    $statusLines = git status --porcelain=v2 --branch 2>$null
    $branch   = ($statusLines | Where-Object { $_ -match '^# branch\.head ' })     -replace '^# branch\.head ', ''
    $headFull = ($statusLines | Where-Object { $_ -match '^# branch\.oid ' })      -replace '^# branch\.oid ', ''
    $upstream = ($statusLines | Where-Object { $_ -match '^# branch\.upstream ' }) -replace '^# branch\.upstream ', ''
    $ab       = ($statusLines | Where-Object { $_ -match '^# branch\.ab ' })       -replace '^# branch\.ab ', ''

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

# Pretty log — scrollable via less
function global:gl {
    script:Write-LogHeader
    git log --color=always `
        '--format=tformat:%C(yellow)%H%C(reset)%C(auto)%d%C(reset)%n%C(brightblack)%an <%ae>  %ad%C(reset)%n%n%C(249)%w(0,4,4)%B%C(reset)' `
        '--date=format:%a %d/%m/%y %H:%M' `
        @args | less -RX
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
# Uses fetch + FETCH_HEAD rather than remote update + origin/<branch>
# so it works even when the tracking ref isn't configured (e.g. uat, hotfix branches).
function global:gitremote-reset {
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git reset --hard FETCH_HEAD
}
function global:remote! {
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git reset --hard FETCH_HEAD
}

function global:grh    { git reset --hard @args }


# Rebase helpers
function global:grd    { git rebase develop @args }
function global:grm    { git rebase master @args }
function global:gro {
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git rebase FETCH_HEAD @args
}
function global:groi {
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git rebase -i FETCH_HEAD @args
}
function global:grod {
    git fetch origin develop
    git rebase FETCH_HEAD @args
}

# Fetch a remote branch then rebase onto it — works even without a local tracking ref.
# Usage: grof epic/policy-list-...   OR   grof origin/epic/policy-list-...
function global:grof {
    param([Parameter(Mandatory)][string]$Branch)
    $remote = $Branch -replace '^origin/', ''
    git fetch origin $remote
    git rebase FETCH_HEAD
}
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
function global:update {
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git rebase FETCH_HEAD
}
function global:updated {
    git fetch origin develop
    git rebase FETCH_HEAD
}

# Clear screen + status
function global:rs { Clear-Host; git status }

# Returns the project root from any worktree within that project.
# Supports two layouts:
#   Standard repo  — main worktree has a .git directory → main worktree IS the root
#   Bare-ish repo  — main worktree has a .git file / no .git  → parent of main worktree is the root
function script:Get-WorktreeProjectRoot {
    $main = git worktree list --porcelain |
        Where-Object { $_ -match '^worktree ' } |
        Select-Object -First 1 |
        ForEach-Object { $_ -replace '^worktree ', '' }
    if (Test-Path (Join-Path $main '.git') -PathType Container) {
        return $main
    }
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

# Fix repos missing a fetch refspec (common when set up via git remote add rather than git clone).
# Scans all direct subdirectories of C:\repos, adds the standard refspec if absent, then fetches.
function global:fix-fetch-refspecs {
    param([string]$Root = 'C:\repos')
    Get-ChildItem $Root -Directory | ForEach-Object {
        $fetch = git -C $_.FullName config --get-all remote.origin.fetch 2>$null
        if (-not $fetch) {
            Write-Host "Fixing: $($_.Name)" -ForegroundColor Yellow
            git -C $_.FullName config remote.origin.fetch "+refs/heads/*:refs/remotes/origin/*"
            git -C $_.FullName remote update
        }
    }
}

function New-GitWorktreeSetup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string[]]$Branches,

        [Parameter()]
        [string]$Name,

        [Parameter()]
        [string]$CloneUrl,

        [Parameter()]
        [string]$Path = (Get-Location).Path
    )

    # ── Clone if a URL was provided ──────────────────────────────────────────
    if ($CloneUrl) {
        # Derive repo name from URL (strip .git suffix)
        $repoName = [System.IO.Path]::GetFileNameWithoutExtension($CloneUrl.TrimEnd('/').Split('/')[-1])
        if (-not $Name) { $Name = $repoName }

        $cloneTarget = Join-Path $Path "$Name\.git-main"

        Write-Host "Cloning $CloneUrl into $cloneTarget..." -ForegroundColor Cyan

        # Clone as a bare repo so worktrees don't fight over a working tree
        git clone --bare $CloneUrl $cloneTarget

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Clone failed."
            return
        }

        # Fix up the remote fetch refspec so `git fetch` works normally from bare clones
        git -C $cloneTarget config remote.origin.fetch '+refs/heads/*:refs/remotes/origin/*'
        git -C $cloneTarget fetch --all --quiet

        $gitRoot = $cloneTarget
    }
    else {
        # ── Use existing repo ────────────────────────────────────────────────
        $gitRoot = git -C $Path rev-parse --show-toplevel 2>$null
        if (-not $gitRoot) {
            Write-Error "Not inside a git repository: $Path. Use -CloneUrl to clone one."
            return
        }

        if (-not $Name) {
            $Name = Split-Path $gitRoot -Leaf
        }
    }

    # ── Create top-level workspace directory ─────────────────────────────────
    # Workspace sits alongside the .git-main folder (or the existing repo's parent)
    $parentDir    = Split-Path $gitRoot -Parent
    $workspaceDir = $parentDir  # worktree subdirs go directly in the named folder

    if ($CloneUrl) {
        # $parentDir is already $Path\$Name — worktrees go there too
        $workspaceDir = $parentDir
    }
    else {
        $workspaceDir = Join-Path (Split-Path $gitRoot -Parent) $Name
        if (Test-Path $workspaceDir) {
            Write-Error "Workspace directory already exists: $workspaceDir"
            return
        }
        New-Item -ItemType Directory -Path $workspaceDir | Out-Null
    }

    Write-Host "Workspace: $workspaceDir" -ForegroundColor Green

    # ── Add a worktree for each branch ────────────────────────────────────────
    foreach ($branch in $Branches) {
        $safeName    = $branch -replace '[/\\]', '-'
        $worktreeDir = Join-Path $workspaceDir $safeName

        $localExists  = git -C $gitRoot rev-parse --verify $branch           2>$null
        $remoteExists = git -C $gitRoot rev-parse --verify "origin/$branch"  2>$null

        if ($localExists) {
            Write-Host "  [$branch] existing local branch" -ForegroundColor Cyan
            git -C $gitRoot worktree add $worktreeDir $branch
        }
        elseif ($remoteExists) {
            Write-Host "  [$branch] tracking remote branch" -ForegroundColor Cyan
            git -C $gitRoot worktree add --track -b $branch $worktreeDir "origin/$branch"
        }
        else {
            Write-Host "  [$branch] new branch" -ForegroundColor Yellow
            git -C $gitRoot worktree add -b $branch $worktreeDir
        }

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "  Failed to create worktree for '$branch'"
        }
        else {
            Write-Host "    -> $worktreeDir" -ForegroundColor DarkGray
        }
    }

    # ── Summary ───────────────────────────────────────────────────────────────
    Write-Host ""
    Write-Host "Done. Layout:" -ForegroundColor Green
    Write-Host "  $workspaceDir"
    Write-Host "    .git-main\  (bare repo)"
    Get-ChildItem $workspaceDir -Directory | Where-Object { $_.Name -ne '.git-main' } | ForEach-Object {
        Write-Host "    $($_.Name)\"
    }
}

Set-Alias wt-setup New-GitWorktreeSetup
