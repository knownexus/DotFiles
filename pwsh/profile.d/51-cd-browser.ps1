# cd folder browser — mirrors zsh 88-cd-browser

<#
Tab on a `cd`/`pushd` line opens a one-column folder browser under the prompt
instead of PSReadLine's MenuComplete grid. It lists directories only, one
level at a time, and the command line follows along as you move:

  Up/Down      move the highlight
  Right        go into the highlighted folder
  Left         go up a level (Backspace too, once the filter is empty)
  typing       filter the current level by prefix
  Enter / Tab  put the highlighted folder on the line and close
  Esc          close and put the line back the way it was

The top row of every level below the start is "." (this folder), so → then
Enter picks the folder you just went into.

Shift+Tab on a cd line (no browser open) drops the last folder off the path.
Every other line keeps plain MenuComplete on both keys.

MenuComplete can't be reused for this: its menu reads its own keys, so ←/→
can't be remapped there (they move between grid columns).
#>

if (Get-Module -Name PSReadLine) {

$script:_CdbE   = [char]27
$script:_CdbSep = [char[]]@('\', '/')
$script:_CdbMaxRows = 15

# Split the argument typed so far into the folder part (kept as typed, so the
# line stays readable) and the partial name being filtered on.
function script:Split-CdBrowserArg([string]$Text) {
    if ($Text -in '~', '..') { $Text += '\' }
    if ($Text -match '^[A-Za-z]:$') { $Text += '\' }
    $i = $Text.LastIndexOfAny($script:_CdbSep)
    if ($i -lt 0) { return @{ Prefix = ''; Filter = $Text } }
    @{ Prefix = $Text.Substring(0, $i + 1); Filter = $Text.Substring($i + 1) }
}

# Full filesystem path for a folder prefix ('' is the current directory), or
# $null when it doesn't resolve to a folder.
function script:Resolve-CdBrowserDir([string]$Prefix) {
    if ($PWD.Provider.Name -ne 'FileSystem' -and -not [IO.Path]::IsPathRooted($Prefix)) { return $null }
    $p = $Prefix
    if ($p -eq '') { return $PWD.ProviderPath }
    if ($p -match '^~(?=$|[\\/])') { $p = $HOME + $p.Substring(1) }
    if (-not [IO.Path]::IsPathRooted($p)) { $p = Join-Path $PWD.ProviderPath $p }
    try { $full = [IO.Path]::GetFullPath($p) } catch { return $null }
    if (Test-Path -LiteralPath $full -PathType Container) { $full } else { $null }
}

# The prefix one level up from $Prefix, or $null at a drive/share root.
# Relative prefixes stay relative (repos\DotFiles\ -> repos\, '' -> ..\);
# ~\ and the like switch to the absolute parent.
function script:Get-CdBrowserParent([string]$Prefix) {
    $dir = Resolve-CdBrowserDir $Prefix
    if (-not $dir) { return $null }
    $parent = [IO.Directory]::GetParent($dir.TrimEnd($script:_CdbSep))
    if (-not $parent -or $dir -eq [IO.Path]::GetPathRoot($dir)) { return $null }

    $trim = $Prefix.TrimEnd($script:_CdbSep)
    if ($trim -eq '') { return '..\' }
    $i    = $trim.LastIndexOfAny($script:_CdbSep)
    $last = $trim.Substring($i + 1)
    if ($last -eq '..') { return $Prefix + '..\' }
    if ($last -notin '.', '~' -and $last -notmatch '^[A-Za-z]:$') {
        if ($i -ge 0) { return $trim.Substring(0, $i + 1) }
        if (-not [IO.Path]::IsPathRooted($trim)) { return '' }
    }
    $parent.FullName.TrimEnd($script:_CdbSep) + '\'
}

# Folder names in $Dir starting with $Filter. Dot-folders only show once the
# filter itself starts with a dot.
function script:Get-CdBrowserItems([string]$Dir, [string]$Filter) {
    $hidden = $Filter.StartsWith('.')
    try {
        @(Get-ChildItem -LiteralPath $Dir -Directory -Force:$hidden -ErrorAction Stop |
            Where-Object {
                $_.Name.StartsWith($Filter, [StringComparison]::OrdinalIgnoreCase) -and
                ($hidden -or -not $_.Name.StartsWith('.'))
            } |
            Sort-Object Name |
            ForEach-Object Name)
    } catch { @() }
}

# Quote a path for the command line when it holds anything PowerShell would
# split or expand. A trailing \ inside single quotes is literal, so it's safe.
function script:ConvertTo-CdBrowserArg([string]$Path) {
    if ($Path -match "[\s'`"`$&;(){},@#|<>``]") { "'" + $Path.Replace("'", "''") + "'" } else { $Path }
}

# The argument a Tab/Shift+Tab on this line would act on, or $null when the
# line isn't a plain `cd [path]` with the cursor at its end.
function script:Get-CdBrowserTarget {
    $ast = $null; $tokens = $null; $errs = $null; $cursor = $null
    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$ast, [ref]$tokens, [ref]$errs, [ref]$cursor)
    $line = $ast.Extent.Text
    if ($cursor -ne $line.Length) { return $null }
    if ($ast.EndBlock.Statements.Count -ne 1) { return $null }
    $cmds = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.CommandAst] }, $true))
    if ($cmds.Count -ne 1) { return $null }
    $cmd = $cmds[0]
    if ($cmd.GetCommandName() -notin 'cd', 'pushd') { return $null }
    $els = $cmd.CommandElements
    if ($els.Count -eq 1) {
        if ($cursor -eq 0 -or -not [char]::IsWhiteSpace($line[$cursor - 1])) { return $null }
        return @{ Start = $cursor; Length = 0; Text = '' }
    }
    if ($els.Count -ne 2) { return $null }
    $arg = $els[1]
    if ($arg -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) { return $null }
    if ($arg.Extent.EndOffset -ne $cursor -or $arg.Value.StartsWith('-')) { return $null }
    @{ Start = $arg.Extent.StartOffset; Length = $cursor - $arg.Extent.StartOffset; Text = $arg.Value }
}

function script:Invoke-CdBrowser($Target) {
    $rl    = [Microsoft.PowerShell.PSConsoleReadLine]
    $e     = $script:_CdbE
    $start = $Target.Start
    $len   = $Target.Length
    $orig  = $Target.Text
    $origArg = if ($len -gt 0) { (Get-CdBrowserLineText).Substring($start, $len) } else { '' }

    $split  = Split-CdBrowserArg $orig
    $prefix = $split.Prefix
    $filter = $split.Filter
    if (-not (Resolve-CdBrowserDir $prefix)) { return $false }

    # Replace the argument span on the line, keeping track of its new length.
    $setArg = {
        param([string]$Text)
        $rl::Replace($start, $script:_cdbLen, $Text)
        $script:_cdbLen = $Text.Length
    }
    $script:_cdbLen = $len

    $state = @{ Items = @(); Sel = 0; Top = 0; Dir = '' }
    $load = {
        param([string]$Select)
        $state.Dir   = Resolve-CdBrowserDir $prefix
        $names       = @(Get-CdBrowserItems $state.Dir $filter)
        $state.Items = @(if ($prefix -ne '' -and $filter -eq '') { '.' }) + $names
        $state.Sel   = 0
        $state.Top   = 0
        if ($Select) {
            $i = [array]::IndexOf($state.Items, $Select)
            if ($i -ge 0) { $state.Sel = $i }
        }
        & $setArg (ConvertTo-CdBrowserArg ($prefix + $filter))
    }

    # Predictions would draw their list over ours; turn them off while open.
    $predictions = (Get-PSReadLineOption).PredictionSource
    if ($predictions -ne 'None') { Set-PSReadLineOption -PredictionSource None }

    & $load
    # A partial name with exactly one match goes straight in, like plain Tab would.
    if ($filter -ne '' -and $state.Items.Count -eq 1) {
        $prefix += $state.Items[0] + '\'; $filter = ''
        & $load
    }

    $rows = [Math]::Max(3, [Math]::Min($script:_CdbMaxRows, [Console]::WindowHeight - 4))
    $state.Reserved = 0      # blank rows made below the line so far
    $state.Drawn    = $null  # first console row of the last draw

    # Make sure there are $Need blank rows below the line. PSReadLine does the
    # scrolling (add blank lines to the buffer, then take them back out) since
    # it keeps its own record of where the prompt starts, which a manual
    # scroll would throw off. Only ever grows, so the list doesn't jump about.
    $reserve = {
        param([int]$Need)
        if ($Need -le $state.Reserved) { return }
        $line = Get-CdBrowserLineText
        $rl::Replace($line.Length, 0, "`n" * $Need)
        $rl::Replace($line.Length, $Need, '')
        $state.Reserved = $Need
    }
    $clear = {
        if ($null -eq $state.Drawn) { return }
        $x = [Console]::CursorLeft; $y = [Console]::CursorTop
        $last = [Math]::Min($state.Drawn + $state.Reserved, [Console]::BufferHeight) - 1
        # The line may have wrapped into the old first row since; leave it be.
        for ($r = [Math]::Max($state.Drawn, $y + 1); $r -le $last; $r++) {
            [Console]::SetCursorPosition(0, $r); [Console]::Write("$e[2K")
        }
        [Console]::SetCursorPosition($x, $y)
        $state.Drawn = $null
    }
    $draw = {
        & $clear
        # header + list (or its "(no ...)" line) + key hint
        & $reserve ([Math]::Min($rows, [Math]::Max(1, $state.Items.Count)) + 2)
        $x = [Console]::CursorLeft; $y = [Console]::CursorTop
        $top   = $y + 1
        $avail = [Console]::BufferHeight - $top
        if ($avail -lt 2) { return }
        $width = [Console]::WindowWidth - 1
        $fit   = { param($s) if ($s.Length -gt $width) { $s.Substring(0, [Math]::Max(0, $width - 1)) + '…' } else { $s } }
        $items = $state.Items
        if ($state.Sel -lt $state.Top) { $state.Top = $state.Sel }
        if ($state.Sel -ge $state.Top + $rows) { $state.Top = $state.Sel - $rows + 1 }

        $out = [Text.StringBuilder]::new()
        $hdr = $state.Dir.TrimEnd($script:_CdbSep)
        if ($hdr.StartsWith($HOME, [StringComparison]::OrdinalIgnoreCase)) { $hdr = '~' + $hdr.Substring($HOME.Length) }
        if ($filter) { $hdr += "  [$filter]" }
        if ($state.Top -gt 0) { $hdr += "  ↑$($state.Top) more" }
        # Long paths lose their start rather than the folder you're in.
        if ($hdr.Length -gt $width) { $hdr = '…' + $hdr.Substring($hdr.Length - $width + 1) }
        [void]$out.Append("$e[2K$e[90m$hdr$e[0m")
        for ($i = 0; $i -lt $rows -and $i + 1 -lt $avail; $i++) {
            $n = $state.Top + $i
            if ($n -ge [Math]::Max(1, $items.Count)) { break }
            [void]$out.Append("`n$e[2K")
            if ($items.Count -eq 0) {
                $msg = if ($filter) { '  (no matches)' } else { '  (no subfolders)' }
                [void]$out.Append("$e[90m$msg$e[0m")
            } else {
                $label = if ($items[$n] -eq '.') { '.  (this folder)' } else { $items[$n] + '\' }
                $label = & $fit "  $label"
                if ($n -eq $state.Sel) { [void]$out.Append("$e[7m$label$e[0m") }
                else { [void]$out.Append("$e[1;34m$label$e[0m") }
            }
        }
        $more = $items.Count - ($state.Top + $rows)
        $hint = '↑↓ move  → open  ← up  Enter pick  Esc cancel'
        if ($more -gt 0) { $hint = "↓$more more   $hint" }
        if ($i + 1 -lt $avail) { [void]$out.Append("`n$e[2K$e[90m$(& $fit $hint)$e[0m") }

        [Console]::SetCursorPosition(0, $top)
        [Console]::Write($out.ToString())
        [Console]::SetCursorPosition($x, $y)
        $state.Drawn = $top
    }

    $restoreLine = $null
    [Console]::CursorVisible = $false
    # Off inside key handlers, which would turn Ctrl+C into a signal ReadKey never sees.
    $ctrlC = [Console]::TreatControlCAsInput
    [Console]::TreatControlCAsInput = $true
    try {
        while ($true) {
            & $draw
            $k = [Console]::ReadKey($true)
            $items = $state.Items
            $pick  = if ($items.Count) { $items[$state.Sel] } else { $null }
            switch ($k.Key) {
                'UpArrow'   { if ($state.Sel -gt 0) { $state.Sel-- }; continue }
                'DownArrow' { if ($state.Sel -lt $items.Count - 1) { $state.Sel++ }; continue }
                'PageUp'    { $state.Sel = [Math]::Max(0, $state.Sel - $rows); continue }
                'PageDown'  { $state.Sel = [Math]::Max(0, [Math]::Min($items.Count - 1, $state.Sel + $rows)); continue }
                'Home'      { $state.Sel = 0; continue }
                'End'       { $state.Sel = [Math]::Max(0, $items.Count - 1); continue }
                'RightArrow' {
                    if ($pick -and $pick -ne '.') { $prefix += $pick + '\'; $filter = ''; & $load }
                    continue
                }
                { $_ -eq 'LeftArrow' -or ($_ -eq 'Backspace' -and $filter -eq '') } {
                    $up = Get-CdBrowserParent $prefix
                    if ($null -ne $up) {
                        $child = Split-Path -Leaf (Resolve-CdBrowserDir $prefix).TrimEnd($script:_CdbSep)
                        $prefix = $up; $filter = ''
                        & $load $child
                    }
                    continue
                }
                'Backspace' { $filter = $filter.Substring(0, $filter.Length - 1); & $load; continue }
                { $_ -in 'Enter', 'Tab' } {
                    $final = if (-not $pick) { $prefix + $filter }
                             elseif ($pick -eq '.') { $prefix }
                             else { $prefix + $pick + '\' }
                    $restoreLine = ConvertTo-CdBrowserArg $final
                    break
                }
                'Escape' { $restoreLine = $origArg; break }
                default {
                    if ($k.KeyChar -eq [char]3 -or ($k.Key -eq 'C' -and $k.Modifiers -band [ConsoleModifiers]::Control)) {
                        $restoreLine = $origArg; break
                    }
                    $ch = $k.KeyChar
                    if ($ch -in '\', '/') {
                        if ($pick -and $pick -ne '.' -and $filter) { $prefix += $pick + '\'; $filter = ''; & $load }
                    } elseif ($ch -and -not [char]::IsControl($ch)) {
                        $filter += $ch; & $load
                    }
                    continue
                }
            }
            if ($null -ne $restoreLine) { break }
        }
    } finally {
        & $clear
        [Console]::TreatControlCAsInput = $ctrlC
        [Console]::CursorVisible = $true
        if ($predictions -ne 'None') { Set-PSReadLineOption -PredictionSource $predictions }
        if ($null -eq $restoreLine) { $restoreLine = $origArg }
        & $setArg $restoreLine
    }
    $true
}

function script:Get-CdBrowserLineText {
    $line = $null; $cursor = $null
    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
    $line
}

Set-PSReadLineKeyHandler -Key Tab -BriefDescription CdBrowserOrMenuComplete `
    -Description 'cd/pushd: one-level folder browser; elsewhere MenuComplete' -ScriptBlock {
    param($key, $arg)
    $target = Get-CdBrowserTarget
    if (-not $target -or -not (Invoke-CdBrowser $target)) {
        [Microsoft.PowerShell.PSConsoleReadLine]::MenuComplete($key, $arg)
    }
}

Set-PSReadLineKeyHandler -Key Shift+Tab -BriefDescription CdUpOrMenuComplete `
    -Description 'cd/pushd: drop the last folder off the path; elsewhere MenuComplete' -ScriptBlock {
    param($key, $arg)
    $target = Get-CdBrowserTarget
    if (-not $target) {
        [Microsoft.PowerShell.PSConsoleReadLine]::MenuComplete($key, $arg)
        return
    }
    $split = Split-CdBrowserArg $target.Text
    # A half-typed name goes first; after that each press is one level up.
    $up = if ($split.Filter -ne '') { $split.Prefix } else { Get-CdBrowserParent $split.Prefix }
    if ($null -ne $up) {
        [Microsoft.PowerShell.PSConsoleReadLine]::Replace($target.Start, $target.Length, (ConvertTo-CdBrowserArg $up))
    }
}

}
