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
    Show-ProfileAliases (Join-Path $script:RESDIR 'profile.d\10-ls.ps1')       'ls aliases'
    Show-ProfileAliases (Join-Path $script:RESDIR 'profile.d\11-aliases.ps1')   'General aliases'
    Show-ProfileAliases (Join-Path $script:RESDIR 'profile.d\80-functions.ps1') 'Functions'
    Write-Host ""
}

function global:gitaliases {
    $esc    = [char]27
    $cyan   = "${esc}[36m"
    $yellow = "${esc}[33m"
    $gray   = "${esc}[90m"
    $reset  = "${esc}[0m"

    # Reads the "git" table out of the shared commands.yaml (also consumed
    # by the zsh side's gitaliases) via yq, instead of keeping a second
    # hand-maintained copy of the same ~90-entry table here.
    $yamlPath = Join-Path $script:SHAREDDIR 'commands.yaml'
    if (-not (Get-Command yq -ErrorAction SilentlyContinue)) {
        Write-Host "gitaliases: yq not found -- install it (winget install MikeFarah.yq) to see this table" -ForegroundColor Yellow
        return
    }

    $sectionNames = @(& yq -r '.git[].section' $yamlPath)
    $sections = [ordered]@{}
    for ($i = 0; $i -lt $sectionNames.Count; $i++) {
        $rows = @(& yq -r ".git[$i].commands[] | [.keys, .cmd, .desc] | @tsv" $yamlPath)
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
Set-Alias -Name cheat -Value allaliases

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
    param([string]$Root = 'C:\repos')
    $any = $false
    Get-ChildItem $Root -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $dir = $_.FullName
        if (-not (git -C $dir rev-parse --is-inside-work-tree 2>$null)) { return }
        $dirty = git -C $dir status --porcelain 2>$null
        $ahead = git -C $dir rev-list --count '@{upstream}..HEAD' 2>$null
        $parts = @()
        if ($dirty) { $parts += "$((@($dirty)).Count) uncommitted" }
        if ($ahead -and [int]$ahead -gt 0) { $parts += "$ahead unpushed" }
        if ($parts.Count -gt 0) {
            $any = $true
            Write-Host "  $($_.Name): $($parts -join ', ')"
        }
    }
    if (-not $any) { Write-Host "All repos under $Root are clean and pushed." -ForegroundColor Green }
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
