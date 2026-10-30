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

# Full terminal reset — mirrors zsh's r='reset'. Clear-Host only clears the
# visible screen; it does nothing about terminal state left corrupted by,
# e.g., cat-ing a binary file (garbled colors, stuck echo mode, wrong
# scroll region). ESC c is the VT "Full Reset" (RIS) sequence, which
# reinitializes the terminal driver the same way Unix's `reset` does --
# supported by Windows Terminal's VT parser.
Remove-Item Alias:r -Force -ErrorAction SilentlyContinue
function global:r {
    [Console]::Out.Write("$([char]27)c")
}

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
    $target = if ($args.Count -gt 0) { $args[0] } else { $HOME }
    # A stray backtick/quote/space on the end (a slipped key or a clipped
    # paste) — drop it when the path only exists without it
    if ($target -is [string] -and -not (Test-Path -LiteralPath $target)) {
        $trimmed = $target.TrimEnd('`', '"', "'", ' ')
        if ($trimmed -and $trimmed -ne $target -and (Test-Path -LiteralPath $trimmed)) { $target = $trimmed }
    }
    $prev = $PWD
    try {
        Push-Location $target -ErrorAction Stop
    } catch [System.Management.Automation.ItemNotFoundException] {
        Write-CdNotFound ([string]$target)
        return
    }
    $global:OLDPWD = $prev
}

# A short "no such folder" for cd, naming the deepest part of the path that
# does exist and any folders there that look like what was meant — instead of
# Push-Location's error pointing into the profile bundle.
function script:Write-CdNotFound([string]$Target) {
    Write-Host "cd: no such folder: $Target" -ForegroundColor Red
    if ($PWD.Provider.Name -ne 'FileSystem' -and -not [IO.Path]::IsPathRooted($Target)) { return }
    $p = $Target -replace '^~(?=$|[\\/])', $HOME
    if (-not [IO.Path]::IsPathRooted($p)) { $p = Join-Path $PWD.ProviderPath $p }
    try { $p = [IO.Path]::GetFullPath($p) } catch { return }

    $missing = ''
    while ($p -and -not (Test-Path -LiteralPath $p -PathType Container)) {
        $missing = Split-Path $p -Leaf
        $p = Split-Path $p -Parent
    }
    if (-not $p) { return }
    Write-Host "    exists up to: $p" -ForegroundColor DarkGray
    $stem = $missing.Substring(0, [Math]::Min(3, $missing.Length))
    if (-not $stem) { return }
    $near = @(Get-ChildItem -LiteralPath $p -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name.StartsWith($stem, [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 5 -ExpandProperty Name)
    if ($near) { Write-Host "    did you mean: $($near -join ', ')" -ForegroundColor DarkGray }
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
function global:rpa { Set-Location 'C:\repos\AccessControl' }
function global:rpap { Set-Location 'C:\repos\admin_portal' }
function global:rpd { Set-Location 'C:\repos\DotFiles' }
function global:rpj { Set-Location 'C:\repos\Joiners' }
function global:rpm { Set-Location 'C:\repos\mobileApp' }
function global:rpn { Set-Location 'C:\repos\NexusMD-Mono' }
function global:rps { Set-Location 'C:\repos\SwimApi' }
function global:rpp { Set-Location 'C:\repos\Pathweave' }

# Editor (respects $VISUAL / $EDITOR env vars)
function global:v {
    $editor = if ($env:VISUAL) { $env:VISUAL } elseif ($env:EDITOR) { $env:EDITOR } else { 'vim' }
    & $editor @args
}

function global:vb {
    $editor = if ($env:VISUAL) { $env:VISUAL } elseif ($env:EDITOR) { $env:EDITOR } else { 'vim' }
    & $editor (Join-Path $script:RESDIR 'profile.d')
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
function global:merge-explorer { & (Join-Path $script:RESDIR 'scripts\consolidate-explorer.ps1') }
