# Git rebase

function global:gri {
    if (-not (Assert-GitRepo)) { return }
    param([string]$Target = '1')
    if ($Target -match '^\d+$') {
        git rebase -i "HEAD~$Target"
    } else {
        git rebase -i "$Target^"
    }
}

function global:grim {
    if (-not (Assert-GitRepo)) { return }
    param([string]$Target = '1')
    if ($Target -match '^\d+$') {
        git rebase -i --rebase-merges "HEAD~$Target"
    } else {
        git rebase -i --rebase-merges "$Target^"
    }
}

# Rebase onto a branch found by partial name
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

# Fetch a remote branch then rebase onto it
# Usage: grof epic/policy-list-...   OR   grof origin/epic/policy-list-...
function global:grof {
    param([Parameter(Mandatory)][string]$Branch)
    $remote = $Branch -replace '^origin/', ''
    git fetch origin $remote
    git rebase FETCH_HEAD
}

# Rebase onto common targets
function global:grd  { if (-not (Assert-GitRepo)) { return }; git rebase develop @args }
function global:grm  { if (-not (Assert-GitRepo)) { return }; git rebase master @args }

function global:gro {
    if (-not (Assert-GitRepo)) { return }
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git rebase FETCH_HEAD @args
}
function global:groi {
    if (-not (Assert-GitRepo)) { return }
    $branch = git rev-parse --abbrev-ref HEAD
    git fetch origin $branch
    git rebase -i FETCH_HEAD @args
}
function global:grod {
    if (-not (Assert-GitRepo)) { return }
    git fetch origin develop
    git rebase FETCH_HEAD @args
}

# Rebase control
function global:gitra  { git rebase --abort }
function global:gitra2 { git rebase --abort }
function global:gitrc  { git rebase --continue }
function global:groot  { if (-not (Assert-GitRepo)) { return }; git rebase -i --root master }
