# Shell history and behaviour settings — mirrors zsh 30-settings

# A command taking at least this many seconds triggers the long-command
# desktop notification in 40-prompt.ps1 (mirrors REPORTTIME).
$global:ReportTime = 10

if (Get-Module -ListAvailable -Name PSReadLine -ErrorAction SilentlyContinue) {
    Import-Module PSReadLine -ErrorAction SilentlyContinue

    # Deduplicate history (mirrors HIST_IGNORE_ALL_DUPS)
    Set-PSReadLineOption -HistoryNoDuplicates

    # Keep 2000 entries in memory, save 1000 (mirrors HISTSIZE/SAVEHIST)
    Set-PSReadLineOption -MaximumHistoryCount 2000

    # Save to a dedicated file (mirrors HISTFILE)
    # Guard: setting HistorySavePath clears in-memory history and reloads from disk,
    # so skip it on profile reloads (sb) when it is already pointing at the right file.
    $historyPath = "$HOME\.pwsh_history"
    if ((Get-PSReadLineOption).HistorySavePath -ne $historyPath) {
        Set-PSReadLineOption -HistorySavePath $historyPath
    }

    # Write each command as it's entered (mirrors INC_APPEND_HISTORY)
    Set-PSReadLineOption -HistorySaveStyle SaveIncrementally

    # Cursor moves to end after history search (mirrors HIST_SEARCH_END)
    Set-PSReadLineOption -HistorySearchCursorMovesToEnd

    # Ignore commands that start with a space (mirrors HIST_IGNORE_SPACE)
    Set-PSReadLineOption -AddToHistoryHandler {
        param([string]$Line)
        return $Line.Length -gt 0 -and $Line[0] -ne ' '
    }

    # Emacs-style line editing (mirrors bindkey -e)
    Set-PSReadLineOption -EditMode Emacs

    # No audible beep on tab-complete misses etc. (mirrors unsetopt BEEP)
    Set-PSReadLineOption -BellStyle None
}
