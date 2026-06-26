# Git core — status, blame, add, archive, commit, diff, restore, show, reset, misc

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
function global:gdf  { git diff @args }
function global:gdfs { git diff --staged @args }
function global:gdf1 { git diff HEAD~1 @args }

# Restore
function global:gres   { git checkout -- @args }
function global:gitres { git checkout -- @args }
function global:gress  { git restore --staged @args }

# Cached rm
function global:gitrmc { git rm --cached @args }

# Show
function global:gs    { git show @args }
function global:gits  { git show @args }
function global:gitsn { git show --name-only @args }

# Reset
function global:grh { git reset --hard @args }
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
