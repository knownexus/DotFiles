# Git core — status, blame, add, archive, commit, diff, restore, show, reset, misc

# Shared pager — runs git and pipes it through less, swallowing the broken-pipe
# error PowerShell raises when you quit less before git has finished writing
# ("Program 'git.exe' failed to run: The pipe is being closed"). That is normal
# and expected when paging: less exits, git's stdout goes away mid-write.
# $PSNativeCommandUseErrorActionPreference (PS 7.3+) turns that into a
# terminating error, so it has to be caught rather than ignored.
# No param() block on purpose: with one, PowerShell tries to bind tokens like
# --color=always as parameter names and fails. A simple function passes
# everything through $args untouched, which is what git needs.
function global:Invoke-GitPaged {
    # Scoped to this function: stops PS 7.3+ turning a native command's
    # failure into a terminating error in the first place. The try/catch below
    # still covers PS 5.1 and any case where the error escapes anyway.
    $PSNativeCommandUseErrorActionPreference = $false
    $ErrorActionPreference = 'Continue'
    try {
        & git @args | less -R
    } catch {
        if ($_.Exception.Message -notmatch 'pipe|closed') { throw }
    }
}

# Shared guard — returns $true if inside a git repo, otherwise prints a message and returns $false.
function global:Assert-GitRepo {
    if (git rev-parse --git-dir 2>$null) { return $true }
    $esc   = [char]27
    $reset = "${esc}[0m"
    $red   = "${esc}[31m"
    $gray  = "${esc}[90m"
    Write-Host "${red}Not a git repository.${reset} ${gray}Navigate into a repo first.${reset}"
    return $false
}

# Status
# In-progress operation (rebase / merge / cherry-pick / revert / am / bisect).
#
# Plain `git status` prints this prominently ("interactive rebase in progress;
# onto bf25f47"), and gstat replaces git status -- so without it you lose the
# single most important thing on screen when a rebase stops on a conflict.
#
# State lives in files under the git dir; `git rev-parse --git-path` resolves
# them correctly inside worktrees, where .git is a file rather than a folder.
function global:Write-GitProgressState {
    $esc    = [char]27
    $reset  = "${esc}[0m"
    $bold   = "${esc}[1m"
    $yellow = "${esc}[33m"
    $gray   = "${esc}[90m"

    # Scriptblocks rather than nested functions: a nested `function script:X`
    # leaks into the session scope and has to be re-declared on every call.
    $GitPath = {
        param([string]$name)
        $v = git rev-parse --git-path $name 2>$null
        if ($v) { return ([string]$v).Trim() }
        return $null
    }
    $ReadFirst = {
        param([string]$path)
        if ($path -and (Test-Path -LiteralPath $path)) {
            return (Get-Content -LiteralPath $path -TotalCount 1 -ErrorAction SilentlyContinue)
        }
        return $null
    }

    $label = $null; $detail = ''; $hint = ''

    $rm = & $GitPath 'rebase-merge'
    $ra = & $GitPath 'rebase-apply'

    if ($rm -and (Test-Path $rm -PathType Container)) {
        $num   = & $ReadFirst (Join-Path $rm 'msgnum')
        $end   = & $ReadFirst (Join-Path $rm 'end')
        $head  = & $ReadFirst (Join-Path $rm 'head-name')
        $onto  = & $ReadFirst (Join-Path $rm 'onto')
        if ($onto) { $onto = git rev-parse --short=7 $onto 2>$null }
        $label = if (Test-Path (Join-Path $rm 'interactive')) { 'REBASE -i' } else { 'REBASE' }
        $branchName = if ($head) { $head -replace '^refs/heads/', '' } else { '' }
        $detail = "$(if($num){$num}else{'?'})/$(if($end){$end}else{'?'})  onto $onto  ($branchName)"
        $hint   = 'gitrc = continue   gitra = abort   git rebase --skip'
    }
    elseif ($ra -and (Test-Path $ra -PathType Container)) {
        $num  = & $ReadFirst (Join-Path $ra 'next')
        $last = & $ReadFirst (Join-Path $ra 'last')
        $head = & $ReadFirst (Join-Path $ra 'head-name')
        if (Test-Path (Join-Path $ra 'applying')) {
            $label = 'AM'
            $hint  = 'git am --continue   git am --abort'
        } else {
            $label = 'REBASE'
            $hint  = 'gitrc = continue   gitra = abort   git rebase --skip'
        }
        $branchName = if ($head) { "  ($($head -replace '^refs/heads/', ''))" } else { '' }
        $detail = "$(if($num){$num}else{'?'})/$(if($last){$last}else{'?'})$branchName"
    }
    elseif ((& $GitPath 'MERGE_HEAD') -and (Test-Path (& $GitPath 'MERGE_HEAD'))) {
        $label  = 'MERGE'
        $detail = "merging $(git rev-parse --short=7 MERGE_HEAD 2>$null)"
        $hint   = 'git merge --continue   git merge --abort'
    }
    elseif ((& $GitPath 'CHERRY_PICK_HEAD') -and (Test-Path (& $GitPath 'CHERRY_PICK_HEAD'))) {
        $label  = 'CHERRY-PICK'
        $detail = "picking $(git rev-parse --short=7 CHERRY_PICK_HEAD 2>$null)"
        $hint   = 'git cherry-pick --continue   git cherry-pick --abort'
    }
    elseif ((& $GitPath 'REVERT_HEAD') -and (Test-Path (& $GitPath 'REVERT_HEAD'))) {
        $label  = 'REVERT'
        $detail = "reverting $(git rev-parse --short=7 REVERT_HEAD 2>$null)"
        $hint   = 'git revert --continue   git revert --abort'
    }
    elseif ((& $GitPath 'BISECT_LOG') -and (Test-Path (& $GitPath 'BISECT_LOG'))) {
        $label  = 'BISECT'
        $steps  = @(git bisect log 2>$null | Where-Object { $_ -match '^git bisect (good|bad)' }).Count
        $detail = "$steps step(s) recorded"
        $hint   = 'git bisect good / bad   git bisect reset'
    }

    if (-not $label) { return $false }

    Write-Host "${bold}${yellow}${label}${reset} ${detail}"
    Write-Host "${gray}        ${hint}${reset}"
    return $true
}

function global:gstat {
    if (-not (Assert-GitRepo)) { return }
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
    $null = Write-GitProgressState
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

# Blame
function global:gbl { git blame @args }

# Add
function global:ga    { git add @args }
function global:gita  { git add @args }
function global:gau   { git add -u @args }
function global:gitau { git add -u @args }

# Whitespace-ignoring staged apply
function global:gaw {
    if (-not (Assert-GitRepo)) { return }
    git diff -w | git apply --cached --ignore-whitespace
}
function global:gaw1 {
    param([Parameter(Mandatory)][string]$Include)
    if (-not (Assert-GitRepo)) { return }
    git diff -w | git apply --cached --ignore-whitespace "--include=$Include"
}

# Archive
function global:gar {
    param([Parameter(Mandatory = $false)][string]$RepoName)

    if (-not (git rev-parse --is-inside-work-tree 2>$null)) {
        Write-Error "Not inside a git repository."
        return
    }

    if (-not $RepoName) {
        $RepoName = Split-Path -Leaf (git rev-parse --show-toplevel)
    }

    $RepoName = ($RepoName -replace '\s+', '_')

    $hash = git rev-parse --short HEAD
    $out  = "$RepoName-$hash.zip"

    git archive --format=zip HEAD -o $out
    Write-Host "Created $out ($([math]::Round((Get-Item $out).Length / 1MB, 1)) MB)"
}

# Commit
# gc is a built-in alias for Get-Content — remove it so our version wins
Remove-Item -Path Alias:gc -Force -ErrorAction SilentlyContinue
function global:git-commit { git commit @args }
Set-Alias -Name gc -Value git-commit -Force -Option AllScope
function global:gitca  { git commit --amend @args }
function global:gca    { git commit --amend @args }
function global:gitcm  { git commit -m @args }
function global:uncommit { git reset --soft HEAD^ }

# Diff
function global:gdf  { Invoke-GitPaged diff --color=always @args }
# Side-by-side in vim instead of inline in the pager (steps through each file).
# --trust-exit-code makes :cq (quit vim with an error) abort the whole run;
# without it git ignores the tool's exit code and walks every remaining file.
function global:gdv  { git difftool --no-prompt --trust-exit-code --tool=vimdiff @args }
function global:gdvs { git difftool --no-prompt --trust-exit-code --tool=vimdiff --staged @args }

# gsv is a built-in alias for Get-Service, and aliases outrank functions —
# remove it or the function below is unreachable. (95-alias-guards.ps1 also
# sweeps for this, but keep the local guard so this file stands alone.)
Remove-Item -Path Alias:gsv -Force -ErrorAction SilentlyContinue

# gs, side by side: a commit against its parent (<rev>^! is git's shorthand).
# Merges show no diff under ^!, so fall back to the first parent for those.
function global:gsv {
    $rev = if ($args.Count -gt 0) { $args[0] } else { 'HEAD' }
    $isMerge = (git rev-list --no-walk --count --merges $rev 2>$null)
    if ($isMerge -eq '1') {
        Write-Host "gsv: $rev is a merge — diffing against its first parent"
        git difftool --no-prompt --trust-exit-code --tool=vimdiff "$rev^1" $rev
    } else {
        git difftool --no-prompt --trust-exit-code --tool=vimdiff "$rev^!"
    }
}
function global:gdfs { Invoke-GitPaged diff --staged --color=always @args }
function global:gdf1 { Invoke-GitPaged diff HEAD~1 --color=always @args }

# Restore
function global:gres   { git checkout -- @args }
function global:gitres { git checkout -- @args }
function global:gress  { git restore --staged @args }

# Cached rm
function global:gitrmc { git rm --cached @args }

# Show
function global:gs    { Invoke-GitPaged show --color=always @args }
function global:gits  { Invoke-GitPaged show --color=always @args }
function global:gitsn { Invoke-GitPaged show --name-only --color=always @args }
function global:gsl   { Invoke-GitPaged show --color=always @args }

# Reset — confirms before discarding uncommitted changes
function global:grh {
    if (-not (Assert-GitRepo)) { return }
    $dirty = git status --short
    if ($dirty) {
        Write-Host "This will discard the following uncommitted changes:" -ForegroundColor Yellow
        Write-Host $dirty
    }
    $target = if ($args.Count -gt 0) { $args[0] } else { 'HEAD' }
    $ans = Read-Host "Proceed with git reset --hard $target? (y/n)"
    if ($ans -eq 'y') { git reset --hard @args }
}
function global:gup { git reset HEAD~1 }

# Local-only ignore via .git/info/exclude (never committed)
function global:localignore {
    if (-not $args) { Write-Warning 'Usage: localignore <pattern> [pattern ...]'; return }
    if (-not (Assert-GitRepo)) { return }
    $gitCommonDir = (git rev-parse --git-common-dir) -replace '/', '\'
    $exclude      = Join-Path $gitCommonDir 'info\exclude'
    foreach ($pattern in $args) {
        Add-Content -Path $exclude -Value $pattern
        Write-Host "Added '$pattern' to .git/info/exclude"
    }
}

# Assume-unchanged
function global:ignoreme {
    if (-not (Assert-GitRepo)) { return }
    $root  = (git rev-parse --show-toplevel) -replace '/', '\'
    $files = $args | ForEach-Object { Get-Item $_ } | ForEach-Object {
        [System.IO.Path]::GetRelativePath($root, $_.FullName) -replace '\\', '/'
    }
    if (-not $files) { return }
    git update-index --assume-unchanged @files
}
function global:dontignoreme {
    if (-not (Assert-GitRepo)) { return }
    $root  = (git rev-parse --show-toplevel) -replace '/', '\'
    $files = $args | ForEach-Object { Get-Item $_ } | ForEach-Object {
        [System.IO.Path]::GetRelativePath($root, $_.FullName) -replace '\\', '/'
    }
    if (-not $files) { return }
    git update-index --no-assume-unchanged @files
}

# Clear screen + status
function global:rs { Clear-Host; git status }
