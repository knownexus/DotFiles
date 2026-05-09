# Key bindings — mirrors zsh 81-key-bindings

if (Get-Module -ListAvailable -Name PSReadLine -ErrorAction SilentlyContinue) {
    Import-Module PSReadLine -ErrorAction SilentlyContinue

    # Ctrl+Right / Ctrl+Left word navigation (mirrors ^[[1;5C / ^[[1;5D)
    Set-PSReadLineKeyHandler -Chord Ctrl+RightArrow -Function ForwardWord
    Set-PSReadLineKeyHandler -Chord Ctrl+LeftArrow  -Function BackwardWord

    # History search with Up/Down arrow (mirrors zsh history-search-backward/forward)
    Set-PSReadLineKeyHandler -Key UpArrow   -Function HistorySearchBackward
    Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward

    # Ctrl+D: exit when buffer is empty, otherwise delete char (mirrors clever_logout_widget)
    Set-PSReadLineKeyHandler -Chord Ctrl+d -ScriptBlock {
        param($key, $arg)
        $line   = $null
        $cursor = $null
        [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
        if ($line.Length -eq 0) {
            [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
            exit
        } else {
            [Microsoft.PowerShell.PSConsoleReadLine]::DeleteChar()
        }
    }
}
