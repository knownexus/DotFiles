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
