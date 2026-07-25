# zoxide integration — inert until zoxide is installed. Mirrors the zsh
# side's zoxide integration (z <partial-name> to jump to a frecent
# directory). zoxide's own init script hooks in via Set-Location/
# Push-Location/Pop-Location, not by touching `prompt`, so it coexists
# cleanly with the custom prompt in 40-prompt.ps1.
if (Get-Command zoxide -ErrorAction SilentlyContinue) {
    Invoke-Expression (zoxide init powershell | Out-String)
}
