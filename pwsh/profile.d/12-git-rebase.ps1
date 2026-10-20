# Git rebase

function global:gri {
    param([string]$Target = '1')
    if (-not (Assert-GitRepo)) { return }
    if ($Target -match '^\d+$') {
        git rebase -i "HEAD~$Target"
    } else {
        git rebase -i "$Target^"
    }
}

function global:grim {
    param([string]$Target = '1')
    if (-not (Assert-GitRepo)) { return }
    if ($Target -match '^\d+$') {
        git rebase -i --rebase-merges "HEAD~$Target"
    } else {
        git rebase -i --rebase-merges "$Target^"
    }
}

# ── Interactive rebase onto main/master, origin, or this branch's own source ──

# This repo's primary branch — main or master, whichever it actually has.
# origin/HEAD decides when it points at one of the two (it's what the remote
# itself calls primary — a develop-default repo's origin/HEAD is ignored);
# otherwise local branches are checked before origin/, or origin/ first with
# -PreferOrigin. Returns $null if neither exists anywhere.
function script:Get-GitPrimaryBranch {
    param([switch]$PreferOrigin)
    $head = git symbolic-ref --quiet --short refs/remotes/origin/HEAD 2>$null
    if ($head) {
        $name = $head -replace '^origin/', ''
        if ($name -in @('main', 'master')) { return $name }
    }
    $order = if ($PreferOrigin) { @('refs/remotes/origin', 'refs/heads') } else { @('refs/heads', 'refs/remotes/origin') }
    foreach ($ns in $order) {
        foreach ($name in @('main', 'master')) {
            git show-ref --verify --quiet "$ns/$name" 2>$null
            if ($LASTEXITCODE -eq 0) { return $name }
        }
    }
    return $null
}

# True if $Candidate could be where $Branch was branched from: it resolves to
# a commit, isn't HEAD or a bare hash (neither names a branch), and isn't
# $Branch itself or its own copy on a remote — a plain tracking checkout
# records "Created from origin/<branch>", which says nothing about where the
# work actually started.
function script:Test-GitSourceCandidate {
    param([string]$Branch, [string]$Candidate)
    if (-not $Candidate -or $Candidate -eq 'HEAD' -or $Candidate -eq $Branch) { return $false }
    if ($Candidate -cmatch '^[0-9a-f]{7,40}$') { return $false }
    if ($Branch) {
        foreach ($remote in @(git remote 2>$null)) {
            if ($Candidate -eq "$remote/$Branch") { return $false }
        }
    }
    git rev-parse --verify --quiet "$Candidate^{commit}" *> $null
    return ($LASTEXITCODE -eq 0)
}

# Works out which branch the current one was created from. In order:
#   1. the branch's own reflog — its oldest entry is "branch: Created from X"
#   2. if X was just HEAD (plain `git checkout -b`/`switch -c`), the HEAD
#      reflog's "checkout: moving from Y to <branch>" at that same commit
#   3. wt.default-base, then develop, then main/master — first that exists,
#      locally or on origin/
# Reflogs expire (90 days by default), so old branches land on step 3.
# Returns @{ Name; How } or $null.
function script:Resolve-GitBranchSource {
    $branch = git symbolic-ref --quiet --short HEAD 2>$null

    if ($branch) {
        $last = @(git reflog show --format=%H%x09%gs "refs/heads/$branch" -- 2>$null) | Select-Object -Last 1
        if ($last -and $last -match "^([0-9a-f]+)`tbranch: Created from (.+)$") {
            $createdSha = $Matches[1]
            $from       = $Matches[2]
            if ($from -eq 'HEAD') {
                $from   = $null
                $prefix = "$createdSha`tcheckout: moving from "
                $suffix = " to $branch"
                foreach ($line in @(git reflog show --format=%H%x09%gs HEAD -- 2>$null)) {
                    if ($line.StartsWith($prefix) -and $line.EndsWith($suffix)) {
                        $from = $line.Substring($prefix.Length, $line.Length - $prefix.Length - $suffix.Length)
                    }
                }
            }
            # Tracking checkouts record the full ref (refs/remotes/origin/x).
            if ($from) { $from = $from -replace '^refs/(heads|remotes)/', '' }
            if (Test-GitSourceCandidate $branch $from) {
                return [pscustomobject]@{ Name = $from; How = 'from reflog' }
            }
        }
    }

    $configured = git config --get wt.default-base 2>$null
    foreach ($name in @($configured, 'develop', (Get-GitPrimaryBranch))) {
        if (-not $name) { continue }
        foreach ($ref in @($name, "origin/$name")) {
            if (Test-GitSourceCandidate $branch $ref) {
                return [pscustomobject]@{ Name = $ref; How = 'fallback' }
            }
        }
    }
    return $null
}

# The commit where HEAD forked off $Source. Checks both the local and origin/
# copies of the source (whichever exist) and takes the fork point nearest
# HEAD, so a stale local develop doesn't drag its newer upstream commits into
# the rebase todo list.
function script:Get-GitForkPoint {
    param([string]$Source)
    $refs   = @($Source)
    $remote = @(git remote 2>$null) | Where-Object { $Source.StartsWith("$_/") } | Select-Object -First 1
    if ($remote) {
        $local = $Source.Substring($remote.Length + 1)
        git show-ref --verify --quiet "refs/heads/$local" 2>$null
        if ($LASTEXITCODE -eq 0) { $refs += $local }
    } else {
        git show-ref --verify --quiet "refs/remotes/origin/$Source" 2>$null
        if ($LASTEXITCODE -eq 0) { $refs += "origin/$Source" }
    }

    $best = $null; $bestCount = [int]::MaxValue
    foreach ($ref in $refs) {
        $mb = git merge-base HEAD $ref 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $mb) { continue }
        $n = [int](git rev-list --count "$mb..HEAD")
        if ($n -lt $bestCount) { $best = $mb; $bestCount = $n }
    }
    return $best
}

# Interactive rebase of the current branch onto main/master, whichever this
# repo has. Always uses origin/<name> when it exists (a local main/master is
# usually stale); only falls back to the local branch if origin has no copy.
# No fetch — that's gromi.
function global:grmi {
    if (-not (Assert-GitRepo)) { return }
    $primary = Get-GitPrimaryBranch -PreferOrigin
    if (-not $primary) { Write-Error "No main or master branch found (origin/ or local) — try gitf first."; return }
    $target = "origin/$primary"
    git show-ref --verify --quiet "refs/remotes/origin/$primary" 2>$null
    if ($LASTEXITCODE -ne 0) {
        $target = $primary
        Write-Host "No 'origin/$primary' — using local '$target'" -ForegroundColor DarkGray
    }
    git rebase -i $target @args
}

# Fetch origin's main/master (whichever it has) and interactively rebase the
# current branch onto it.
function global:gromi {
    if (-not (Assert-GitRepo)) { return }
    $primary = Get-GitPrimaryBranch -PreferOrigin
    if (-not $primary) { Write-Error "No main or master branch found on origin — try gitf first."; return }
    git fetch origin $primary
    if ($LASTEXITCODE -ne 0) { return }
    git rebase -i FETCH_HEAD @args
}

# Interactive rebase of just this branch's own commits, from the point where
# it left its source branch — the base stays put, so the todo list is exactly
# the work done on this branch, ready to squash/reword/reorder. The source is
# worked out automatically (see Resolve-GitBranchSource) or given as -Source;
# anything else is passed to git rebase.
function global:grsi {
    param([string]$Source)
    if (-not (Assert-GitRepo)) { return }
    $gitArgs = @($args)
    if ($Source -like '-*') { $gitArgs = @($Source) + $gitArgs; $Source = '' }

    if ($Source) {
        $Source = Resolve-WtPrimaryBranch $Source @()
        $how    = 'given'
    } else {
        $found = Resolve-GitBranchSource
        if (-not $found) { Write-Error "Couldn't work out where this branch started — pass it: grsi <source-branch>"; return }
        $Source = $found.Name
        $how    = $found.How
    }
    git rev-parse --verify --quiet "$Source^{commit}" *> $null
    if ($LASTEXITCODE -ne 0) { Write-Error "Unknown branch or ref: '$Source'"; return }

    $base = Get-GitForkPoint $Source
    if (-not $base) { Write-Error "No common history with '$Source'."; return }
    $count = [int](git rev-list --count "$base..HEAD")
    if ($count -eq 0) {
        Write-Host "Nothing to rebase — no commits since this branch left '$Source'." -ForegroundColor DarkGray
        return
    }
    $short = git rev-parse --short $base
    Write-Host "Source: $Source ($how) — $count commit(s) since $short" -ForegroundColor DarkGray
    git rebase -i $base @gitArgs
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

function global:grom {
    if (-not (Assert-GitRepo)) { return }
    git fetch origin master
    git rebase FETCH_HEAD @args
}

function global:gromm {
    if (-not (Assert-GitRepo)) { return }
    git fetch origin main
    git rebase FETCH_HEAD @args
}

# Rebase control
function global:gitra  { git rebase --abort }
function global:gitra2 { git rebase --abort }
function global:gitrc  { git rebase --continue }
function global:groot  { if (-not (Assert-GitRepo)) { return }; git rebase -i --root master }
