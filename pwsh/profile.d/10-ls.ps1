# ls aliases — mirrors zsh 10-ls

$script:_E      = [char]27
$script:_Reset  = "$($script:_E)[0m"
$script:_Bold   = "$($script:_E)[1m"
$script:_Blue   = "$($script:_E)[1;34m"   # directories
$script:_Green  = "$($script:_E)[32m"     # executables / scripts
$script:_Yellow = "$($script:_E)[33m"     # archives
$script:_Cyan   = "$($script:_E)[36m"     # images
$script:_Gray   = "$($script:_E)[90m"     # hidden items / chrome

function script:Get-HumanSize([long]$Bytes) {
    if ($Bytes -ge 1TB) { return '{0,6:N1}T' -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return '{0,6:N1}G' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0,6:N1}M' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0,6:N1}K' -f ($Bytes / 1KB) }
    return '{0,6}B' -f $Bytes
}

function script:Get-EntryColor([System.IO.FileSystemInfo]$Item) {
    $hidden = $Item.Attributes -band [System.IO.FileAttributes]::Hidden
    if ($Item -is [System.IO.DirectoryInfo]) {
        if ($hidden) { return $script:_Gray } else { return $script:_Blue }
    }
    if ($hidden) { return $script:_Gray }
    $ext = $Item.Extension.ToLower()
    if ($ext -in @('.exe', '.cmd', '.bat', '.ps1', '.sh', '.msi')) { return $script:_Green }
    if ($ext -in @('.zip', '.tar', '.gz', '.7z', '.rar', '.bz2', '.xz')) { return $script:_Yellow }
    if ($ext -in @('.png', '.jpg', '.jpeg', '.gif', '.bmp', '.svg', '.ico', '.webp')) { return $script:_Cyan }
    return $script:_Reset
}

function script:Write-WideList([System.IO.FileSystemInfo[]]$Items) {
    if (-not $Items -or $Items.Count -eq 0) { return }
    $width = [Math]::Max(1, $Host.UI.RawUI.WindowSize.Width)
    $names = @($Items | ForEach-Object {
        if ($_ -is [System.IO.DirectoryInfo]) { $_.Name + '/' } else { $_.Name }
    })
    $colW = [int](($names | Measure-Object Length -Maximum).Maximum) + 2
    $cols = [Math]::Max(1, [Math]::Floor($width / $colW))
    for ($i = 0; $i -lt $Items.Count; $i++) {
        $color   = script:Get-EntryColor $Items[$i]
        $display = $names[$i].PadRight($colW)
        Write-Host "$color$display$script:_Reset" -NoNewline
        if ((($i + 1) % $cols) -eq 0) { Write-Host '' }
    }
    if ($Items.Count % $cols -ne 0) { Write-Host '' }
}

function script:Write-LongList([System.IO.FileSystemInfo[]]$Items) {
    if (-not $Items -or $Items.Count -eq 0) { return }
    $header = ' {0,-7} {1,-12} {2,7}  {3}' -f 'Mode', 'Modified', 'Size', 'Name'
    Write-Host "$script:_Bold$script:_Gray$header$script:_Reset"
    Write-Host "$script:_Gray $('-' * 40)$script:_Reset"
    foreach ($item in $Items) {
        $color  = script:Get-EntryColor $item
        $mode   = $item.Mode
        $date   = $item.LastWriteTime.ToString('MMM dd HH:mm')
        $size   = if ($item -is [System.IO.DirectoryInfo]) { '  <dir>' } else { script:Get-HumanSize $item.Length }
        $name   = if ($item -is [System.IO.DirectoryInfo]) { $item.Name + '/' } else { $item.Name }
        $chrome = ' {0,-7} {1,-12} {2,7}  ' -f $mode, $date, $size
        Write-Host "$script:_Gray$chrome$script:_Reset$color$name$script:_Reset"
    }
    $dc = ($Items | Where-Object { $_ -is [System.IO.DirectoryInfo] }).Count
    $fc = ($Items | Where-Object { $_ -is [System.IO.FileInfo] }).Count
    Write-Host "$script:_Gray  $dc $(if ($dc -eq 1) {'dir'} else {'dirs'}), $fc $(if ($fc -eq 1) {'file'} else {'files'})$script:_Reset"
}

function script:Write-GridList([System.IO.FileSystemInfo[]]$Items) {
    if (-not $Items -or $Items.Count -eq 0) { return }
    $width  = [Math]::Max(40, $Host.UI.RawUI.WindowSize.Width)
    $colW   = [Math]::Floor($width / 2)
    $chromeW = 31   # ' mode    date         size    ' fixed width
    $nameW  = [Math]::Max(4, $colW - $chromeW)

    $rows = [Math]::Ceiling($Items.Count / 2)

    function script:Format-GridCell($item, $nameW) {
        $color  = script:Get-EntryColor $item
        $mode   = $item.Mode
        $date   = $item.LastWriteTime.ToString('MMM dd HH:mm')
        $size   = if ($item -is [System.IO.DirectoryInfo]) { '  <dir>' } else { script:Get-HumanSize $item.Length }
        $name   = if ($item -is [System.IO.DirectoryInfo]) { $item.Name + '/' } else { $item.Name }
        if ($name.Length -gt $nameW) { $name = $name.Substring(0, $nameW - 1) + '~' }
        $chrome = ' {0,-7} {1,-12} {2,7}  ' -f $mode, $date, $size
        return @{ Chrome = $chrome; Color = $color; Name = $name }
    }

    for ($r = 0; $r -lt $rows; $r++) {
        $left = script:Format-GridCell $Items[$r] $nameW
        Write-Host "$script:_Gray$($left.Chrome)$script:_Reset$($left.Color)$($left.Name.PadRight($nameW))$script:_Reset" -NoNewline

        $ri = $r + $rows
        if ($ri -lt $Items.Count) {
            $right = script:Format-GridCell $Items[$ri] $nameW
            Write-Host "$script:_Gray$($right.Chrome)$script:_Reset$($right.Color)$($right.Name)$script:_Reset"
        } else {
            Write-Host ''
        }
    }

    $dc = ($Items | Where-Object { $_ -is [System.IO.DirectoryInfo] }).Count
    $fc = ($Items | Where-Object { $_ -is [System.IO.FileInfo] }).Count
    Write-Host "$script:_Gray  $dc $(if ($dc -eq 1) {'dir'} else {'dirs'}), $fc $(if ($fc -eq 1) {'file'} else {'files'})$script:_Reset"
}

# Override the built-in ls alias so our function wins
Remove-Item Alias:ls -ErrorAction SilentlyContinue

function global:ls  { script:Write-GridList @(Get-ChildItem        @args) }
function global:l   { script:Write-WideList @(Get-ChildItem        @args) }
function global:ll  { script:Write-LongList @(Get-ChildItem        @args) }
function global:la  { script:Write-WideList @(Get-ChildItem -Force @args) }
function global:lla { script:Write-LongList @(Get-ChildItem -Force @args) }
