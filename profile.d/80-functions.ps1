# Utility functions — mirrors zsh 80-functions

# mkdir + cd in one step (mirrors mcd)
function global:mcd {
    param([Parameter(Mandatory)][string]$Path)
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Set-Location $Path
}

# Hex dump — Format-Hex is the PowerShell native equivalent of od -t x4 / od -t x1
function global:odx {
    param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Args)
    Format-Hex @Args
}

function global:odx1 {
    param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Args)
    Format-Hex @Args
}

# Search in C/H source files (mirrors fch)
function global:fch {
    param([Parameter(Mandatory)][string]$Pattern)
    Get-ChildItem -Recurse -Include '*.c', '*.h' -ErrorAction SilentlyContinue |
        Select-String -Pattern $Pattern
}

# Search in C/H/asm source files (mirrors fchs)
function global:fchs {
    param([Parameter(Mandatory)][string]$Pattern)
    Get-ChildItem -Recurse -Include '*.c', '*.h', '*.s', '*.S' -ErrorAction SilentlyContinue |
        Select-String -Pattern $Pattern
}

# Init a new git repo with a licence file (mirrors make-project)
function global:New-GitProject {
    param(
        [Parameter(Mandatory)][string]$Name,
        [ValidateSet('MIT', 'GPL-2', 'GPL-3', 'Apache-2', 'LGPL-2.1')]
        [string]$Licence = 'MIT'
    )
    mcd $Name
    git init
    "This project is licensed under $Licence." | Set-Content COPYING
    git add COPYING
    git commit -m "This project is under the $Licence licence at this time"
}
Set-Alias -Name make-project -Value New-GitProject

# Aliases cheat sheet — print defined aliases grouped by section
function global:Show-ProfileAliases {
    param([string]$File, [string]$Title)
    $lines   = Get-Content $File
    # First pass: collect Set-Alias -Value targets (implementation helpers, not short names)
    $targets = @{}
    foreach ($line in $lines) {
        if ($line -match 'Set-Alias\s+.*-Value\s+(\S+)') { $targets[$matches[1]] = $true }
    }
    # Second pass: collect names grouped by section comment
    $sections = [ordered]@{}
    $current  = ''
    $seen     = @{}
    foreach ($line in $lines) {
        if ($line -match '^# (.+)$') {
            $current = $matches[1]
        } elseif ($line -match '^function global:(\S+)') {
            $name = $matches[1]
            # skip internal helpers and alias-target long-names
            if (-not $targets[$name] -and $name -notmatch '^(Invoke-|Show-)') {
                if (-not $seen[$name]) {
                    $seen[$name] = $true
                    if (-not $sections[$current]) { $sections[$current] = [System.Collections.Generic.List[string]]::new() }
                    $sections[$current].Add($name)
                }
            }
        } elseif ($line -match "^\s*Set-Alias\s+.*-Name\s+(\S+)") {
            $name = $matches[1]
            if (-not $seen[$name]) {
                $seen[$name] = $true
                if (-not $sections[$current]) { $sections[$current] = [System.Collections.Generic.List[string]]::new() }
                $sections[$current].Add($name)
            }
        }
    }
    Write-Host ""
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "  $('─' * ($Title.Length + 1))" -ForegroundColor DarkGray
    foreach ($sec in $sections.Keys) {
        $names = $sections[$sec]
        if (-not $names -or $names.Count -eq 0) { continue }
        Write-Host "   $sec" -ForegroundColor Yellow
        $names | ForEach-Object { Write-Host ("     $_") -ForegroundColor White }
    }
}

function global:aliases {
    Show-ProfileAliases 'C:\repos\pwsh\profile.d\10-ls.ps1'       'ls aliases'
    Show-ProfileAliases 'C:\repos\pwsh\profile.d\11-aliases.ps1'   'General aliases'
    Show-ProfileAliases 'C:\repos\pwsh\profile.d\80-functions.ps1' 'Functions'
    Write-Host ""
}

function global:gitaliases {
    $esc    = [char]27
    $cyan   = "${esc}[36m"
    $yellow = "${esc}[33m"
    $gray   = "${esc}[90m"
    $reset  = "${esc}[0m"

    # Each entry: @(aliases, git-command, description)
    $sections = [ordered]@{
        'Status & info' = @(
            @('gstat  g!  git!',          'git status (custom)',            'colour-coded status with branch and ahead/behind'),
            @('gbl',                      'git blame',                      'annotate each line with last-changing commit'),
            @('gs  gits',                 'git show',                       'inspect a commit, tag, or blob'),
            @('gitsn',                    'git show --name-only',           'list files changed in a commit'),
            @('rs',                       'clear; git status',              'clear screen then show status')
        )
        'Log' = @(
            @('gl  gitl',                 'git log',                        'pretty full log, scrollable via less'),
            @('gl2  gitl2',               'git log --shortstat',            'full log with per-commit file-change counts'),
            @('gitlg',                    'git log --graph',                'branching graph log'),
            @('gitlg2',                   'git log --oneline --graph',      'compact one-line graph log')
        )
        'Staging' = @(
            @('ga  gita',                 'git add',                        'stage files'),
            @('gau  gitau',               'git add -u',                     'stage all changes to tracked files'),
            @('gaw',                      'git diff -w | git apply --cached','stage all changes, ignoring whitespace'),
            @('gaw1 <pattern>',           'git apply --cached --include',   'stage matching files, ignoring whitespace')
        )
        'Diff' = @(
            @('gdf',                      'git diff',                       'show unstaged changes'),
            @('gdfs',                     'git diff --staged',              'show staged changes'),
            @('gdf1',                     'git diff HEAD~1',                'show changes introduced by the last commit')
        )
        'Commit' = @(
            @('gc',                       'git commit',                     'open editor and commit'),
            @('gitcm <msg>',              'git commit -m',                  'commit with an inline message'),
            @('gitca  gca',               'git commit --amend',             'rewrite the last commit'),
            @('uncommit',                 'git reset --soft HEAD^',         'undo last commit, keep changes staged')
        )
        'Restore & reset' = @(
            @('gres  gitres',             'git checkout --',                'discard working tree changes to a file'),
            @('gress',                    'git restore --staged',           'move staged changes back to working tree'),
            @('grh [commit]',             'git reset --hard',               'discard all changes, optionally to a commit'),
            @('gup',                      'git reset HEAD~1',               'undo last commit, leave changes unstaged')
        )
        'Archive & ignore' = @(
            @('gar [name]',               'git archive HEAD',               'zip HEAD into <name>-<hash>.zip'),
            @('localignore <pattern>',    '.git/info/exclude',              'ignore files locally without touching .gitignore'),
            @('ignoreme',                 'git update-index --assume-unchanged',    'stop tracking local changes to a file'),
            @('dontignoreme',             'git update-index --no-assume-unchanged', 'resume tracking local changes to a file'),
            @('gitrmc',                   'git rm --cached',                'remove a file from the index only')
        )
        'Branch & checkout' = @(
            @('gitb',                     'git branch',                     'list or manage branches'),
            @('gitc <branch>',            'git checkout',                   'switch branch or restore files'),
            @('gitcb <branch>',           'git checkout -b',                'create and switch to a new branch'),
            @('gitcd',                    'git checkout develop',           'switch to develop')
        )
        'Stash' = @(
            @('gst  stash',               'git stash push',                 'save dirty state onto the stash'),
            @('gp  stashp',               'git stash pop',                  'restore the most recent stash entry'),
            @('gstl',                     'git stash list',                 'list stash entries')
        )
        'Cherry-pick & clean' = @(
            @('gitcp <commit>',           'git cherry-pick',                'apply a commit onto the current branch'),
            @('gclean',                   'git clean -fd',                  'delete untracked files and directories')
        )
        'Fetch & pull' = @(
            @('gitf',                     'git fetch',                      'download changes from remote'),
            @('gitpl',                    'git pull --rebase',              'fetch and rebase onto upstream'),
            @('update',                   'git fetch + rebase',             'sync current branch with origin'),
            @('updated',                  'git fetch + rebase',             'sync with origin/develop')
        )
        'Push' = @(
            @('gpf  gitfp  gitpf',        'git push --force',               'force-push'),
            @('gp1',                      'git push --set-upstream',        'push and set upstream tracking'),
            @('pushdr <branch>',          'git push -u origin local:remote','push to a differently-named remote branch'),
            @('pushdr-f <branch>',        'git push -uf origin local:remote','force-push to a differently-named remote branch')
        )
        'Remote' = @(
            @('gru',                      'git remote update',              'fetch all remotes'),
            @('grpo',                     'git remote prune origin',        'remove stale remote-tracking refs'),
            @('giturl',                   'git config --get remote.origin.url', 'print the remote URL'),
            @('gitremote-reset  remote!', 'git fetch + reset --hard',       'hard-reset current branch to remote HEAD')
        )
        'Rebase' = @(
            @('gri [n|commit]',           'git rebase -i',                  'interactively rewrite history'),
            @('grim [n|commit]',          'git rebase -i --rebase-merges',  'interactive rebase, preserving merge commits'),
            @('grd',                      'git rebase develop',             'rebase current branch onto develop'),
            @('grm',                      'git rebase master',              'rebase current branch onto master'),
            @('gro',                      'git fetch + rebase FETCH_HEAD',  'sync and rebase onto origin/<current>'),
            @('groi',                     'git fetch + rebase -i FETCH_HEAD','sync and interactive rebase onto origin/<current>'),
            @('grod',                     'git fetch + rebase FETCH_HEAD',  'sync and rebase onto origin/develop'),
            @('grof <branch>',            'git fetch + rebase FETCH_HEAD',  'sync and rebase onto named remote branch'),
            @('rebaseon <fragment>',      'git rebase',                     'rebase onto branch matching partial name'),
            @('gitra  gitra2',            'git rebase --abort',             'abort an in-progress rebase'),
            @('gitrc',                    'git rebase --continue',          'continue after resolving conflicts'),
            @('groot',                    'git rebase -i --root master',    'rebase all commits from the very first')
        )
        'Reverted changes' = @(
            @('grev <A> <B>',             '(custom script)',                'show changes A made that B later undid'),
            @('grev-clean <A> <B>',       '(custom script)',                'rewrite A to remove those redundant changes'),
            @('grev-scan [range]',        '(custom script)',                'scan a commit range for changes later reverted')
        )
        'Worktree' = @(
            @('wt-list',                  'git worktree list',              'list all worktrees'),
            @('wt-go <name>',             'Set-Location',                   'cd to a worktree by name fragment'),
            @('wt-feature <id> <desc>',   'git worktree add -b feature/...','create a feature branch worktree'),
            @('wt-fix <id> <desc>',       'git worktree add -b fix/...',    'create a fix branch worktree'),
            @('wt-c <branch>',            'git worktree add',               'checkout an existing branch as a worktree'),
            @('wt-done <name>',           'git worktree remove + branch -d','remove a worktree and delete its local branch'),
            @('wt-done-f <name>',         'git worktree remove -f + branch -D','force-remove a worktree and delete its branch'),
            @('wt!',                      'git status (all worktrees)',     'show status across every worktree'),
            @('wt-prune',                 'git worktree prune',             'clean up stale worktree references'),
            @('wt-setup <branches>',      '(custom)',                       'single-repo bare clone with branch worktrees'),
            @('wt-workspace',             '(custom script)',                'multi-repo workspace with per-repo worktrees'),
            @('fix-fetch-refspecs [root]','git config + remote update',     'repair missing fetch refspecs across repos')
        )
    }

    # Derive column widths from the data so nothing gets clipped.
    $allEntries  = $sections.Values | ForEach-Object { $_ }
    $aliasWidth  = 2 + ($allEntries | ForEach-Object { $_[0].Length } | Measure-Object -Maximum).Maximum
    $cmdWidth    = 2 + ($allEntries | ForEach-Object { $_[1].Length } | Measure-Object -Maximum).Maximum

    Write-Host ""
    Write-Host "  ${cyan}Git aliases${reset}"
    Write-Host "  ${gray}$('─' * ($aliasWidth + $cmdWidth + 20))${reset}"

    foreach ($section in $sections.Keys) {
        Write-Host ""
        Write-Host "  ${yellow}$section${reset}"
        foreach ($entry in $sections[$section]) {
            $aliases = $entry[0]
            $cmd     = $entry[1]
            $desc    = $entry[2]
            $aliasPad = ' ' * [Math]::Max(1, $aliasWidth - $aliases.Length)
            $cmdPad   = ' ' * [Math]::Max(1, $cmdWidth  - $cmd.Length)
            Write-Host "    $aliases${gray}${aliasPad}${cmd}${cmdPad}${reset}$desc"
        }
    }
    Write-Host ""
}

function global:allaliases {
    aliases
    gitaliases
}

# Find-and-replace across all files under a path (mirrors fr)
function global:fr {
    param(
        [Parameter(Mandatory, Position = 0)][string]$Find,
        [Parameter(Mandatory, Position = 1)][string]$Replace,
        [string]$Path = "."
    )
    Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -and $content.Contains($Find)) {
                $content.Replace($Find, $Replace) | Set-Content $_.FullName -NoNewline
            }
        }
}
