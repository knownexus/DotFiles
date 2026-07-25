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

Get-ChildItem "$script:RESDIR\profile.d\*.ps1" |
    Sort-Object Name |
    ForEach-Object { . $_.FullName }
