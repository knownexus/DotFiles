# Git remote — fetch, pull, push, remote operations

# Fetch / pull
function global:gitf  { git fetch @args }
function global:gitpl { git pull --rebase @args }

# Push (force) — confirms and shows the upstream that would be overwritten
# --force-with-lease refuses the push if the remote has commits your
# remote-tracking ref does not know about, so you cannot silently flatten
# someone else's work. --force-if-includes closes the hole in it: a plain
# lease check passes if you merely FETCHED their commits without integrating
# them, because the tracking ref then looks current. Together they mean
# "overwrite only what I have actually seen and built on".
function script:Confirm-ForcePush {
    $upstream = git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>$null
    if (-not $upstream) { $upstream = '<no upstream configured>' }
    Write-Host "Force-push (with lease) to: $upstream" -ForegroundColor Yellow
    $ans = Read-Host "Proceed? (y/n)"
    if ($ans -ne 'y') { return }

    git push --force-with-lease --force-if-includes @args
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host "If this was rejected as 'stale info', the remote moved since your last" -ForegroundColor Yellow
        Write-Host "fetch - that is the lease doing its job. Run 'gitf' and look at what"   -ForegroundColor Yellow
        Write-Host "arrived before deciding; re-running with --force would discard it."     -ForegroundColor Yellow
    }
}
function global:gpf   { Confirm-ForcePush @args }
function global:gitfp { Confirm-ForcePush @args }
function global:gitpf { Confirm-ForcePush @args }

# Push current branch upstream
function global:gp1 {
    if (-not (Assert-GitRepo)) { return }
    $branch = git rev-parse --abbrev-ref HEAD
    git push --set-upstream origin $branch
}

# Push local branch to a named remote branch
# Usage: pushdr epic/policy-list-...
function global:pushdr {
    param([Parameter(Mandatory)][string]$RemoteBranch)
    if (-not (Assert-GitRepo)) { return }
    $RemoteBranch = $RemoteBranch -replace ' ', '_'
    $local = git rev-parse --abbrev-ref HEAD
    git push -u origin "${local}:${RemoteBranch}"
}

# Force-push local branch to a named remote branch
# Usage: pushdr-f epic/policy-list-...
function global:pushdr-f {
    param([Parameter(Mandatory)][string]$RemoteBranch)
    if (-not (Assert-GitRepo)) { return }
    $RemoteBranch = $RemoteBranch -replace ' ', '_'
    $local = git rev-parse --abbrev-ref HEAD
    git push -uf origin "${local}:${RemoteBranch}"
}

# Remote info
function global:gru    { git remote update }
function global:grpo   { git remote prune origin }
function global:giturl { git config --get remote.origin.url }

# Hard-reset to remote HEAD
# Uses fetch + FETCH_HEAD so it works even without a configured tracking ref.
function global:gitremote-reset {
    if (-not (Assert-GitRepo)) { return }
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git reset --hard FETCH_HEAD
}
function global:remote! {
    if (-not (Assert-GitRepo)) { return }
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git reset --hard FETCH_HEAD
}

# Fetch + rebase shortcuts
function global:update {
    if (-not (Assert-GitRepo)) { return }
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git rebase FETCH_HEAD
}
function global:updated {
    if (-not (Assert-GitRepo)) { return }
    git fetch origin develop
    git rebase FETCH_HEAD
}
