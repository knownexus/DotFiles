#Requires -Version 5.1
# Entry point — mirror of knownexus/zsh
# Clone this repo to ~/.resources/powershell, then add to $PROFILE:
#   . "$HOME\.resources\powershell\Profile.ps1"

$script:RESDIR = Split-Path $MyInvocation.MyCommand.Path -Parent
# Data shared between the zsh and pwsh sides of the dotfiles monorepo lives
# one level up from this repo's own pwsh/ folder, at <repo>/shared.
$script:SHAREDDIR = Join-Path (Split-Path $script:RESDIR -Parent) 'shared'

if (Test-Path "$HOME\bin") {
    $env:PATH = "$HOME\bin;$env:PATH"
}

# Cheap stand-in for Get-Module -ListAvailable, which parses the manifest of
# every module on PSModulePath (300ms+ on a cold start) just to answer "is X
# installed?". A module folder of that name on PSModulePath is enough here.
function global:Test-ModuleInstalled {
    param([Parameter(Mandatory)][string]$Name)
    foreach ($dir in $env:PSModulePath -split [System.IO.Path]::PathSeparator) {
        if ($dir -and (Test-Path -LiteralPath (Join-Path $dir $Name))) { return $true }
    }
    return $false
}

# Dot-sourcing a script file has a fixed cost of ~10-15ms on this machine
# (execution-policy / AMSI checks run per file), which across 25 fragments
# added ~350ms to every launch. Concatenate the fragments into one cached
# bundle and dot-source that instead; the bundle is rebuilt whenever a
# fragment is added, removed or edited. Fragments must not rely on
# $PSScriptRoot -- use $script:RESDIR / $script:SHAREDDIR. Set
# $env:PWSH_PROFILE_NO_BUNDLE=1 to load fragments one by one (exact
# per-file error locations while debugging).
$fragments = @(Get-ChildItem "$script:RESDIR\profile.d\*.ps1" | Sort-Object Name)
if ($env:PWSH_PROFILE_NO_BUNDLE) {
    $fragments | ForEach-Object { . $_.FullName }
}
else {
    $bundle    = Join-Path ([System.IO.Path]::GetTempPath()) 'pwsh-profile-bundle.ps1'
    $bundleSig = "$bundle.sig"
    $sig = ($fragments | ForEach-Object { "$($_.Name)=$($_.LastWriteTimeUtc.Ticks)" }) -join ';'
    $current = if (Test-Path $bundleSig) { [System.IO.File]::ReadAllText($bundleSig) }
    if ($current -ne $sig -or -not (Test-Path $bundle)) {
        $sb = [System.Text.StringBuilder]::new()
        foreach ($f in $fragments) {
            [void]$sb.AppendLine("# ===== profile.d\$($f.Name) =====")
            [void]$sb.AppendLine([System.IO.File]::ReadAllText($f.FullName))
        }
        # BOM so Windows PowerShell 5.1 reads the non-ASCII comments as UTF-8.
        [System.IO.File]::WriteAllText($bundle, $sb.ToString(), [System.Text.UTF8Encoding]::new($true))
        [System.IO.File]::WriteAllText($bundleSig, $sig)
    }
    . $bundle
}
Remove-Variable fragments, bundle, bundleSig, sig, current, sb, f -ErrorAction SilentlyContinue
