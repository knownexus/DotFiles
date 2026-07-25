# direnv integration — inert until direnv is installed. Mirrors the zsh
# side's direnv integration (per-project environment variables auto-loaded
# from a .envrc file).
if (Get-Command direnv -ErrorAction SilentlyContinue) {
    Invoke-Expression (direnv hook pwsh | Out-String)
}
