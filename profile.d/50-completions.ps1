# Tab completion -- mirrors zsh 50-completions

if (Get-Module -ListAvailable -Name PSReadLine -ErrorAction SilentlyContinue) {
    Import-Module PSReadLine -ErrorAction SilentlyContinue

    # Menu-style completion (mirrors zsh list-suffixes / group completion)
    Set-PSReadLineKeyHandler -Key Tab       -Function MenuComplete
    Set-PSReadLineKeyHandler -Key Shift+Tab -Function MenuComplete

    # Show tooltips in the completion menu
    Set-PSReadLineOption -ShowToolTips

    # Use command history as a prediction source (requires PSReadLine 2.1+)
    $rlVersion = (Get-Module PSReadLine).Version
    if ($rlVersion -ge [version]'2.1') {
        Set-PSReadLineOption -PredictionSource History
        Set-PSReadLineOption -PredictionViewStyle ListView
    }
}

# Load posh-git for rich git tab completion if available
if (Get-Module -ListAvailable -Name posh-git -ErrorAction SilentlyContinue) {
    Import-Module posh-git
}

# Reload a completion/argument-completer (mirrors zsh compreload)
function global:compreload {
    param([Parameter(Mandatory)][string]$Command)
    Write-Host "Re-import the module that registers completions for '$Command' to reload them."
}
