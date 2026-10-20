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

<#
Cheatsheets. Every renderer below *emits* its lines rather than
Write-Host-ing them, so `cheat` can pipe the whole ~200-line thing into a
pager instead of dumping it all into the scrollback — which also means the
colours have to be raw ANSI, since Write-Host's -ForegroundColor writes
straight to the host and never reaches the pipeline.

(Top-level "# Comment" lines are section headers to the parser in
Show-ProfileAliases below, so the notes in here are block comments.)
#>
$script:CheatColors = @{
    reset  = "$([char]27)[0m"
    cyan   = "$([char]27)[36m"
    yellow = "$([char]27)[33m"
    gray   = "$([char]27)[90m"
}

<#
Prints the aliases/functions defined in one profile.d file, grouped by the
top-level section comments in it.
#>
function global:Show-ProfileAliases {
    param([string]$File, [string]$Title)
    $c = $script:CheatColors
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
    ""
    "  $($c.cyan)$Title$($c.reset)"
    "  $($c.gray)$('─' * ($Title.Length + 1))$($c.reset)"
    foreach ($sec in $sections.Keys) {
        $names = $sections[$sec]
        if (-not $names -or $names.Count -eq 0) { continue }
        "   $($c.yellow)$sec$($c.reset)"
        foreach ($n in $names) { "     $n" }
    }
}

<#
Renders one top-level group out of the shared commands.yaml (also consumed
by the zsh side) as an aligned three-column table, instead of keeping a
second hand-maintained copy of the same data per platform. Rows carrying
`only: zsh` belong to the other shell and are filtered out here.
#>
function global:Show-YamlCheatsheet {
    param([string]$Group, [string]$Title)
    $c = $script:CheatColors

    $yamlPath = Join-Path $script:SHAREDDIR 'commands.yaml'
    if (-not (Get-Command yq -ErrorAction SilentlyContinue)) {
        "$($c.yellow)$Group`: yq not found -- install it (winget install MikeFarah.yq) to see this table$($c.reset)"
        return
    }

    $sectionNames = @(& yq -r ".${Group}[].section" $yamlPath)
    $sections = [ordered]@{}
    for ($i = 0; $i -lt $sectionNames.Count; $i++) {
        $rows = @(& yq -r ".${Group}[$i].commands[] | select((.only // `"pwsh`") == `"pwsh`") | [.keys, .cmd, .desc] | @tsv" $yamlPath)
        $entries = @()
        foreach ($row in $rows) {
            $parts = $row -split "`t"
            $entries += , @($parts[0], $parts[1], $parts[2])
        }
        $sections[$sectionNames[$i]] = $entries
    }

    # Derive column widths from the data so nothing gets clipped.
    $allEntries  = $sections.Values | ForEach-Object { $_ }
    $aliasWidth  = 2 + ($allEntries | ForEach-Object { $_[0].Length } | Measure-Object -Maximum).Maximum
    $cmdWidth    = 2 + ($allEntries | ForEach-Object { $_[1].Length } | Measure-Object -Maximum).Maximum

    ""
    "  $($c.cyan)$Title$($c.reset)"
    "  $($c.gray)$('─' * ($aliasWidth + $cmdWidth + 20))$($c.reset)"

    foreach ($section in $sections.Keys) {
        if (-not $sections[$section]) { continue }
        ""
        "  $($c.yellow)$section$($c.reset)"
        foreach ($entry in $sections[$section]) {
            $aliases  = $entry[0]
            $cmd      = $entry[1]
            $desc     = $entry[2]
            $aliasPad = ' ' * [Math]::Max(1, $aliasWidth - $aliases.Length)
            $cmdPad   = ' ' * [Math]::Max(1, $cmdWidth  - $cmd.Length)
            "    $aliases$($c.gray)$aliasPad$cmd$cmdPad$($c.reset)$desc"
        }
    }
    ""
}

# Cheatsheets
function global:aliases {
    Show-ProfileAliases (Join-Path $script:RESDIR 'profile.d\10-ls.ps1')       'ls aliases'
    Show-ProfileAliases (Join-Path $script:RESDIR 'profile.d\11-aliases.ps1')   'General aliases'
    Show-ProfileAliases (Join-Path $script:RESDIR 'profile.d\80-functions.ps1') 'Functions'
    ""
}

function global:gitaliases  { Show-YamlCheatsheet 'git'  'Git aliases' }

function global:keybindings { Show-YamlCheatsheet 'keys' 'Key bindings' }
Set-Alias -Name keys -Value keybindings

function global:allaliases {
    aliases
    gitaliases
    keybindings
}

<#
cheat is allaliases behind a pager -- the full set is ~200 lines, which
would otherwise bury everything else in the scrollback. `less -R` (no -X,
deliberately, unlike gl) draws on the alternate screen, so quitting leaves
the terminal exactly as it was.
#>
function global:cheat {
    $out = allaliases
    if (Get-Command less -ErrorAction SilentlyContinue) {
        try { $out | less -R }
        catch { if ($_.Exception.Message -notmatch 'pipe') { throw } }   # quitting less early
    } else {
        $out | Out-Host -Paging
    }
}

<#
doctor reports which optional, tool-gated integrations are active right
now. Several things in this config (colorized man-page-equivalent view,
fzf pickers, zoxide, direnv, desktop notifications) are inert until their
underlying tool is installed.
#>
# Diagnostics
function global:doctor {
    $checks = @(
        @('bat',       'non-markdown files in view'),
        @('glow',      'rendered markdown in view'),
        @('PSFzf',     'Ctrl+T/Ctrl+R, wt-gof, gitcf', $true),
        @('zoxide',    'z <partial-name>'),
        @('direnv',    'per-project .envrc auto-loading'),
        @('BurntToast','desktop notification for long-running commands', $true)
    )
    Write-Host ""
    Write-Host "  Optional integrations" -ForegroundColor Cyan
    Write-Host "  ----------------------" -ForegroundColor DarkGray
    foreach ($c in $checks) {
        $name = $c[0]; $label = $c[1]; $isModule = $c.Count -gt 2 -and $c[2]
        $found = if ($isModule) { [bool](Get-Module -ListAvailable -Name $name) }
                 else { [bool](Get-Command $name -ErrorAction SilentlyContinue) }
        if ($found) {
            Write-Host "  ✓ $name  " -ForegroundColor Green -NoNewline
            Write-Host $label -ForegroundColor DarkGray
        } else {
            Write-Host "  ✗ $name  " -ForegroundColor Red -NoNewline
            Write-Host "$label — not installed" -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

<#
view is a read-only file viewer. Markdown renders properly (headers, bold,
tables) via glow; everything else is syntax-highlighted via bat; falls
back to Get-Content if neither is installed.
#>
# Viewer
function global:view {
    param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Paths)
    foreach ($p in $Paths) {
        if ((Get-Command glow -ErrorAction SilentlyContinue) -and ($p -match '\.md$|\.markdown$')) {
            glow -p -s pink $p
        } elseif (Get-Command bat -ErrorAction SilentlyContinue) {
            bat --paging=always $p
        } else {
            Get-Content $p | Out-Host -Paging
        }
    }
}

<#
repos-status scans a directory of repos and reports which need attention
(uncommitted changes and/or commits not yet pushed to their upstream).
#>
# Workspace
function global:repos-status {
    param([string]$Root)
    if ($Root) { gws status --root $Root }
    else { gws status }
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
