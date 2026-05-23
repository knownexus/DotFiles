# Git aliases — mirrors zsh 12-git
# Note: PowerShell function names cannot contain ! so g! becomes gstat, remote! becomes gitremote-reset.

# Status
# git! is not a valid PS function name — use gstat as the canonical form
function global:gstat  { git status @args }
function global:g!     { git status @args }
function global:git!   { git status @args }

# Add
function global:ga     { git add @args }
function global:gita   { git add @args }

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
function global:git-log { git log @args }
Set-Alias -Name gl   -Value git-log -Force -Option AllScope
Set-Alias -Name gitl -Value git-log -Force -Option AllScope

# Pretty graph log (mirrors gitlg / gitlg2)
function global:gitlg {
    git log --graph --abbrev-commit --decorate `
        '--format=format:%C(bold blue)%h%C(reset) - %C(bold cyan)%aD%C(reset) %C(bold green)(%ar)%C(reset)%C(bold yellow)%d%C(reset)%n          %C(white)%s%C(reset) %C(dim white)- %an%C(reset)' `
        --all @args
}

function global:gitlg2 {
    git log --graph --abbrev-commit --decorate `
        '--format=format:%C(bold blue)%h%C(reset) - %C(bold cyan)%aD%C(reset) %C(bold green)(%ar)%C(reset)%C(bold yellow)%d%C(reset)%n          %C(white)%s%C(reset) %C(dim white)- %an%C(reset)' `
        --all @args
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
function global:stash  { git stash save @args }
function global:stashp { git stash pop }

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
