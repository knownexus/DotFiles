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
    Show-ProfileAliases 'C:\repos\pwsh\profile.d\12-git.ps1' 'Git aliases'
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
