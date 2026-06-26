<#
.SYNOPSIS
    Creates a workspace directory containing multiple git repos, each with worktrees for specified branches.

.DESCRIPTION
    For each repo URL, clones a bare repo into <workspace>/<repo-name>/.bare/, creates a .git
    pointer file so tooling recognises the directory, then adds a worktree for each requested branch.

    Resulting layout:
        <Path>/
          <repo-name>/
            .bare/        <- bare clone
            .git          <- file: "gitdir: ./.bare"
            <branch>/     <- worktree per branch
            ...

.PARAMETER Path
    Root directory to create. Will be created if it does not exist.

.PARAMETER Repos
    One or more remote repo URLs to clone.

.PARAMETER Branches
    Branches to check out as worktrees inside each repo directory.

.EXAMPLE
    New-GitWorkspace.ps1 `
        -Path C:\dev\my-workspace `
        -Repos 'https://github.com/org/api.git', 'https://github.com/org/web.git' `
        -Branches main, develop
#>
param (
    [Parameter(Mandatory)]
    [string] $Path,

    [Parameter(Mandatory)]
    [string[]] $Repos,

    [Parameter(Mandatory)]
    [string[]] $Branches
)

# Resolve to an absolute path before building any child paths.
# Without this, a relative $Path produces relative $worktreePath values, and
# git worktree add resolves them from inside .bare/ — nesting the path incorrectly.
$Path = [System.IO.Path]::GetFullPath($Path)

New-Item -ItemType Directory -Path $Path -Force | Out-Null
Write-Host "Workspace: $Path" -ForegroundColor Cyan

foreach ($repoUrl in $Repos) {
    $repoName = [System.IO.Path]::GetFileNameWithoutExtension(
        $repoUrl.TrimEnd('/').Split('/')[-1]
    )
    $repoDir = Join-Path $Path $repoName
    $bareDir = Join-Path $repoDir '.bare'

    Write-Host "`nCloning $repoName..." -ForegroundColor Yellow

    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null

    git clone --bare $repoUrl $bareDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to clone $repoUrl - skipping."
        continue
    }

    # Point the repo directory at the bare clone so git tooling (IDEs, scripts)
    # works from <repoDir> without needing to cd into .bare.
    Set-Content -Path (Join-Path $repoDir '.git') -Value 'gitdir: ./.bare'

    # Bare clones don't configure fetch refspecs by default; set it so
    # git fetch pulls down remote-tracking branches (needed for worktree add).
    git -C $bareDir config remote.origin.fetch '+refs/heads/*:refs/remotes/origin/*'
    git -C $bareDir fetch --all --quiet

    foreach ($branch in $Branches) {
        $worktreePath = Join-Path $repoDir $branch

        # Check via exit code rather than capturing output — 2>$null doesn't reliably
        # suppress native stderr in PowerShell, causing false truthy values.
        git -C $bareDir rev-parse --verify "origin/$branch" 2>&1 | Out-Null
        $remoteExists = $LASTEXITCODE -eq 0

        if ($remoteExists) {
            Write-Host "  + worktree: $branch (remote)" -ForegroundColor Green
            # DWIM: git auto-creates a local tracking branch when the remote ref exists.
            git -C $bareDir worktree add $worktreePath $branch 2>&1 | Write-Host
        }
        else {
            Write-Host "  + worktree: $branch (new branch)" -ForegroundColor Yellow
            git -C $bareDir worktree add -b $branch $worktreePath 2>&1 | Write-Host
        }

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "  Could not add worktree for '$branch' in $repoName."
        }
    }
}

Write-Host "`nDone. Workspace ready at: $Path" -ForegroundColor Cyan
