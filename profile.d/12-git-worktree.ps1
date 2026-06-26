# Git worktree — worktree management and multi-repo workspace

# Returns the project root from any worktree within that project.
# Supports two layouts:
#   Standard repo — main worktree has a .git directory -> main worktree IS the root
#   Bare-ish repo — main worktree has a .git file / no .git -> parent of main worktree is the root
function global:Get-GitWorktreeRoot {
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
function global:Get-GitWorktreePaths {
    git worktree list --porcelain |
        Where-Object { $_ -match '^worktree ' } |
        ForEach-Object { $_ -replace '^worktree ', '' }
}

function global:wt-go {
    if (-not (Assert-GitRepo)) { return }
    param([Parameter(Mandatory)][string]$Name)
    $match = Get-GitWorktreePaths |
        Where-Object { $_ -like "*$Name*" } |
        Select-Object -First 1
    if ($match) { Set-Location $match } else { Write-Host "No worktree matching '$Name'" }
}

function global:wt-list { if (-not (Assert-GitRepo)) { return }; git worktree list }

# Create a feature worktree at <project-root>\feature\<TicketId>-<Desc>
function global:wt-feature {
    param(
        [Parameter(Mandatory)][string]$TicketId,
        [Parameter(Mandatory)][string]$Desc,
        [string]$Base = 'develop'
    )
    if (-not (Assert-GitRepo)) { return }
    $Desc   = $Desc -replace ' ', '_'
    $root   = Get-GitWorktreeRoot
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
    if (-not (Assert-GitRepo)) { return }
    $Desc   = $Desc -replace ' ', '_'
    $root   = Get-GitWorktreeRoot
    $wtPath = Join-Path $root "fix\$TicketId-$Desc"
    git worktree add $wtPath -b "fix/$TicketId-$Desc" $Base
}

# Checkout an existing branch into a worktree, preserving path structure.
# e.g. wt-c feature/ABC-123-foo -> <project-root>\feature\ABC-123-foo
function global:wt-c {
    param([Parameter(Mandatory)][string]$Branch)
    if (-not (Assert-GitRepo)) { return }
    $root   = Get-GitWorktreeRoot
    $wtPath = Join-Path $root ($Branch -replace '/', '\')
    git worktree add $wtPath $Branch
}

# Remove a worktree and delete its local branch
function global:wt-done {
    param([Parameter(Mandatory)][string]$Name)
    if (-not (Assert-GitRepo)) { return }
    $match = Get-GitWorktreePaths |
        Where-Object { $_ -like "*$Name*" } |
        Select-Object -First 1
    if (-not $match) { Write-Host "No worktree matching '$Name'"; return }
    $branch = git -C $match branch --show-current
    git worktree remove $match
    if ($branch) { git branch -d $branch }
}

function global:wt-done-f {
    param([Parameter(Mandatory)][string]$Name)
    if (-not (Assert-GitRepo)) { return }
    $match = Get-GitWorktreePaths |
        Where-Object { $_ -like "*$Name*" } |
        Select-Object -First 1
    if (-not $match) { Write-Host "No worktree matching '$Name'"; return }
    $branch = git -C $match branch --show-current
    git worktree remove --force $match
    if ($branch) { git branch -D $branch }
}

# Show git status for every worktree in the current repo
function global:wt! {
    if (-not (Assert-GitRepo)) { return }
    Get-GitWorktreePaths | ForEach-Object {
        Write-Host "`n=== $_ ===" -ForegroundColor Cyan
        git -C $_ status --short
    }
}

# Prune stale worktree references
function global:wt-prune { if (-not (Assert-GitRepo)) { return }; git worktree prune -v }

# Fix repos missing a fetch refspec (common when set up via git remote add rather than git clone).
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

# Single-repo worktree setup — clone or use an existing repo and add branch worktrees
function global:New-GitWorktreeSetup {
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

    if ($CloneUrl) {
        $repoName    = [System.IO.Path]::GetFileNameWithoutExtension($CloneUrl.TrimEnd('/').Split('/')[-1])
        if (-not $Name) { $Name = $repoName }

        $cloneTarget = Join-Path $Path "$Name\.git-main"

        Write-Host "Cloning $CloneUrl into $cloneTarget..." -ForegroundColor Cyan

        git clone --bare $CloneUrl $cloneTarget

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Clone failed."
            return
        }

        git -C $cloneTarget config remote.origin.fetch '+refs/heads/*:refs/remotes/origin/*'
        git -C $cloneTarget fetch --all --quiet

        $gitRoot = $cloneTarget
    }
    else {
        $gitRoot = git -C $Path rev-parse --show-toplevel 2>$null
        if (-not $gitRoot) {
            Write-Error "Not inside a git repository: $Path. Use -CloneUrl to clone one."
            return
        }

        if (-not $Name) {
            $Name = Split-Path $gitRoot -Leaf
        }
    }

    $parentDir    = Split-Path $gitRoot -Parent
    $workspaceDir = $parentDir

    if ($CloneUrl) {
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

    foreach ($branch in $Branches) {
        $safeName    = $branch -replace '[/\\]', '-'
        $worktreeDir = Join-Path $workspaceDir $safeName

        $localExists  = git -C $gitRoot rev-parse --verify $branch          2>$null
        $remoteExists = git -C $gitRoot rev-parse --verify "origin/$branch" 2>$null

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

    Write-Host ""
    Write-Host "Done. Layout:" -ForegroundColor Green
    Write-Host "  $workspaceDir"
    Write-Host "    .git-main\  (bare repo)"
    Get-ChildItem $workspaceDir -Directory | Where-Object { $_.Name -ne '.git-main' } | ForEach-Object {
        Write-Host "    $($_.Name)\"
    }
}

Set-Alias wt-setup New-GitWorktreeSetup

# Workspace — clone multiple repos each with their own worktrees
Remove-Item -Path Alias:wt-workspace        -Force -ErrorAction SilentlyContinue
Remove-Item -Path Function:New-GitWorkspace -Force -ErrorAction SilentlyContinue
function global:wt-workspace {
    param (
        [Parameter(Mandatory)][string]   $Path,
        [Parameter(Mandatory)][string[]] $Repos,
        [Parameter(Mandatory)][string[]] $Branches
    )
    & 'C:\repos\pwsh\scripts\New-GitWorkspace.ps1' -Path $Path -Repos $Repos -Branches $Branches
}
