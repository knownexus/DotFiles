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

Remove-Item Alias:r  -Force -ErrorAction SilentlyContinue
Set-Alias -Name r -Value Clear-Host -Force -Option AllScope

# g  — search with full exclusion list
function global:g  { Invoke-Search @args }

# g1 — search with minimal exclusions
function global:g1 { Invoke-Search -ExcludeDirs @('.git', '.claude', '.idea') @args }

# g2 — search excluding .git, node_modules, .claude, .idea
function global:g2 { Invoke-Search -ExcludeDirs @('.git', 'node_modules', '.claude', '.idea') @args }

# rg-based search (ripgrep) — faster, respects .gitignore automatically
# rgs  — search with common noise dirs excluded
# rgs1 — minimal exclusions (just .git, .claude, .idea)
# rgs2 — exclude .git, node_modules, .claude, .idea
function global:rgs {
    $excludeArgs = $script:SearchExcludeDirs | ForEach-Object { "--glob=!$_/**" }
    rg -n --color=always $excludeArgs @args
}
function global:rgs1 {
    rg -n --color=always '--glob=!.git/**' '--glob=!.claude/**' '--glob=!.idea/**' @args
}
function global:rgs2 {
    rg -n --color=always '--glob=!.git/**' '--glob=!node_modules/**' '--glob=!.claude/**' '--glob=!.idea/**' @args
}

# Navigation
function global:cdh  { Set-Location $HOME }
function global:..   { Set-Location .. }
function global:...  { Set-Location ..\.. }
function global:.... { Set-Location ..\..\.. }

$global:OLDPWD = $PWD

<#
cd pushes onto PowerShell's own location stack (mirrors zsh's AUTO_PUSHD) —
Pop-Location (built in) undoes the last one. `cd -` still does the plain
OLDPWD toggle as before; there's no numbered `cd -N` the way zsh has it,
just the single-step Pop-Location.
#>
# Directory stack
Remove-Item -Path Alias:cd -Force -ErrorAction SilentlyContinue
function global:cd {
    if ($args.Count -gt 0 -and $args[0] -eq '-') {
        $tmp = $PWD
        Set-Location $global:OLDPWD
        $global:OLDPWD = $tmp
        return
    }
    $global:OLDPWD = $PWD
    if ($args.Count -gt 0) { Push-Location $args[0] } else { Push-Location $HOME }
}

# AUTO_CD — a bare directory name (no `cd`) changes into it
$ExecutionContext.InvokeCommand.CommandNotFoundAction = {
    param($CommandName, $EventArgs)
    try {
        if (Test-Path -LiteralPath $CommandName -PathType Container) {
            $EventArgs.CommandScriptBlock = { Push-Location $CommandName }.GetNewClosure()
            $EventArgs.StopSearch = $true
        }
    } catch {
        # Never let a bad path (invalid chars etc.) break command lookup itself
    }
}

# Editor
function global:c { claude }

Remove-Item -Path Alias:rp -Force -ErrorAction SilentlyContinue
function global:go-repos { Set-Location 'C:\repos' }
Set-Alias -Name rp -Value go-repos -Force -Option AllScope
function global:rpa { Set-Location 'C:\repos\DriveFurtherAPI\develop' }
function global:rpam { Set-Location 'C:\repos\DriveFurtherAPI\master' }
function global:rpn { Set-Location 'C:\repos\DriveFurtherNucleus\develop' }
function global:rpnm { Set-Location 'C:\repos\DriveFurtherNucleus\master' }
function global:rpc { Set-Location 'C:\repos\CirrusAutomatedTests' }
function global:rpp { Set-Location 'C:\repos\pwsh' }
function global:rpm { Set-Location 'C:\repos\ManagementDashboard' }
function global:rpnc { Set-Location 'C:\repos\NexusCommunity' }

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

<#
rm sends to the Recycle Bin instead of deleting outright (mirrors the zsh
side's trash-based rm, using the real Windows Recycle Bin instead of a
manual folder). Restore with trash-restore <name>, or via Explorer;
permanently clear with trash-empty. Internal config cleanup elsewhere uses
Remove-Item directly so temp files still delete for real.
#>
# Filesystem helpers
Remove-Item -Path Alias:rm -Force -ErrorAction SilentlyContinue
function global:rm {
    param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Paths)
    Add-Type -AssemblyName Microsoft.VisualBasic
    foreach ($p in $Paths) {
        $resolved = Resolve-Path -Path $p -ErrorAction SilentlyContinue
        if (-not $resolved) {
            Write-Error "rm: no such file or directory: $p"
            continue
        }
        foreach ($item in $resolved) {
            $full = $item.Path
            if (Test-Path $full -PathType Container) {
                [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory(
                    $full, 'OnlyErrorDialogs', 'SendToRecycleBin')
            } else {
                [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile(
                    $full, 'OnlyErrorDialogs', 'SendToRecycleBin')
            }
            Write-Host "Trashed (Recycle Bin): $full"
        }
    }
}
function global:rmr { rm @args }

# Trash
function global:trash-restore {
    param([string]$Name)
    $shell = New-Object -ComObject Shell.Application
    $recycleBin = $shell.Namespace(10)
    if (-not $Name) {
        Write-Host "In Recycle Bin:"
        $recycleBin.Items() | ForEach-Object { Write-Host "  $($_.Name)" }
        return
    }
    $item = $recycleBin.Items() | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    if (-not $item) {
        Write-Error "Not found in Recycle Bin: $Name"
        return
    }
    $item.InvokeVerb("undelete")
    Write-Host "Restored: $Name"
}

function global:trash-empty {
    $ans = Read-Host "Permanently empty the Recycle Bin? (y/n)"
    if ($ans -eq 'y') {
        Clear-RecycleBin -Force -ErrorAction SilentlyContinue
        Write-Host "Recycle Bin emptied."
    } else {
        Write-Host "Aborted."
    }
}

<#
cp/mv confirm before overwriting. Note: PowerShell's -Confirm prompts for
every operation (there's no built-in "only if the destination already
exists" mode the way POSIX cp -i has), so this is a bit more talkative
than the zsh equivalent — same safety intent, slightly different UX.
#>
# Copy / move / clipboard
Remove-Item -Path Alias:cp -Force -ErrorAction SilentlyContinue
function global:cp { Copy-Item @args -Confirm }
Remove-Item -Path Alias:mv -Force -ErrorAction SilentlyContinue
function global:mv { Move-Item @args -Confirm }

# copy is a built-in alias for Copy-Item — remove it so our version (pipe
# input to the system clipboard) wins
Remove-Item -Path Alias:copy -Force -ErrorAction SilentlyContinue
function global:copy { $input | Set-Clipboard }

# Filesystem helpers
function global:md  { New-Item -ItemType Directory -Force @args }

function global:touch {
    param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Paths)
    $Paths | ForEach-Object {
        if (Test-Path $_) { (Get-Item $_).LastWriteTime = Get-Date }
        else { New-Item -ItemType File -Path $_ | Out-Null }
    }
}

function global:open { Start-Process @args }

# which — resolve a command to its source path
function global:which { Get-Command @args | Select-Object -ExpandProperty Source }

# path — show PATH entries one per line
function global:path { $env:PATH -split ';' | Where-Object { $_ } }

# psgrep — find a running process by partial name
function global:psgrep {
    param([Parameter(Mandatory)][string]$Name)
    Get-Process | Where-Object { $_.Name -like "*$Name*" }
}

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
function global:brk {
    $width = $Host.UI.RawUI.WindowSize.Width
    Write-Host ([string][char]0x2500 * $width) -ForegroundColor DarkGray
}

# Explorer
function global:merge-explorer { & 'C:\repos\pwsh\scripts\consolidate-explorer.ps1' }
