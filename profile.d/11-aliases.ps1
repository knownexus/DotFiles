# General aliases — mirrors zsh 11-aliases

$script:SearchExcludeDirs = @(
    '.git', 'node_modules', 'obj', 'bin', 'logs',
    'Migrations', 'dist', 'dist_electron', '.vs', '.idea'
)

# Recursive grep with common exclusions (mirrors search_grep / g / g1 / g2)
function global:Invoke-Search {
    param(
        [Parameter(Mandatory, Position = 0)][string]$Pattern,
        [string]$Path = ".",
        [string[]]$ExcludeDirs = $script:SearchExcludeDirs
    )
    $excludeArgs = $ExcludeDirs | ForEach-Object { "--exclude-dir=$_" }
    grep -rn --color=always $excludeArgs $Pattern $Path
}

# g  — search with full exclusion list
function global:g  { Invoke-Search @args }

# g1 — search with minimal exclusions
function global:g1 { Invoke-Search -ExcludeDirs @('.git', '.claude', '.idea') @args }

# g2 — search excluding .git, node_modules, .claude, .idea
function global:g2 { Invoke-Search -ExcludeDirs @('.git', 'node_modules', '.claude', '.idea') @args }

# Navigation
function global:cdh { Set-Location $HOME }

$global:OLDPWD = $PWD

Remove-Item -Path Alias:cd -Force -ErrorAction SilentlyContinue
function global:cd {
    if ($args[0] -eq '-') {
        $tmp = $PWD
        Set-Location $global:OLDPWD
        $global:OLDPWD = $tmp
    } else {
        $global:OLDPWD = $PWD
        Set-Location @args
    }
}

Remove-Item -Path Alias:rp -Force -ErrorAction SilentlyContinue
function global:go-repos { Set-Location 'C:\repos' }
Set-Alias -Name rp -Value go-repos -Force -Option AllScope
function global:rpa { Set-Location 'C:\repos\DriveFurtherAPI' }
function global:rpn { Set-Location 'C:\repos\DriveFurtherNucleus' }
function global:rpc { Set-Location 'C:\repos\CirrusAutomatedTests' }

# Editor (respects $VISUAL / $EDITOR env vars)
function global:v {
    $editor = if ($env:VISUAL) { $env:VISUAL } elseif ($env:EDITOR) { $env:EDITOR } else { 'vim' }
    & $editor @args
}

function global:vb {
    $editor = if ($env:VISUAL) { $env:VISUAL } elseif ($env:EDITOR) { $env:EDITOR } else { 'vim' }
    & $editor 'C:\repos\pwsh\profile.d'
}

# Reload profile (mirrors source ~/.zshrc / sb on Linux)
function global:sb { . $PROFILE }

# Filesystem helpers
function global:rmr { Remove-Item -Recurse -Force @args }
function global:md  { New-Item -ItemType Directory -Force @args }

function global:remspace {
    param([Parameter(Mandatory)][string]$Path)
    (Get-Content $Path) | ForEach-Object { $_.TrimEnd() } | Set-Content $Path
}

# Directory tree, excluding noise dirs
function global:tre {
    param([string]$Path = ".")
    $exclude = $script:SearchExcludeDirs -join '|'
    Get-ChildItem -Path $Path -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "\\($exclude)\\" } |
        Select-Object FullName |
        Format-Table -HideTableHeaders
}
