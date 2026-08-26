# PSFzf integration — inert until the PSFzf module is installed. Mirrors
# the zsh side's fzf integration (Ctrl+T/Ctrl+R bindings, wt-gof, gitcf).
#   Install-Module PSFzf -Scope CurrentUser
if (Get-Module -ListAvailable -Name PSFzf) {

    # Importing PSFzf costs ~300ms, so defer it until Ctrl+T/Ctrl+R is actually
    # pressed instead of paying that on every launch. The placeholder handler
    # imports the module, lets it bind its real handlers over these, then asks
    # for the key again rather than guessing PSFzf's internal function names.
    $script:PSFzfLazyBind = {
        param($key, $arg)
        Remove-PSReadLineKeyHandler -Key Ctrl+t -ErrorAction SilentlyContinue
        Remove-PSReadLineKeyHandler -Key Ctrl+r -ErrorAction SilentlyContinue
        Import-Module PSFzf -ErrorAction SilentlyContinue
        Set-PsFzfOption -PSReadlineChordProvider 'Ctrl+t' -PSReadlineChordReverseHistory 'Ctrl+r'
        Write-Host "`nPSFzf loaded — press the key again" -ForegroundColor DarkGray
        [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
    }
    Set-PSReadLineKeyHandler -Key Ctrl+t -ScriptBlock $script:PSFzfLazyBind
    Set-PSReadLineKeyHandler -Key Ctrl+r -ScriptBlock $script:PSFzfLazyBind

    # Fuzzy-pick a worktree and cd to it
    function global:wt-gof {
        $match = Get-GitWorktreePaths | fzf --prompt="worktree> "
        if ($match) { Set-Location $match }
    }

    # Fuzzy-pick a local/remote branch and check it out
    function global:gitcf {
        $local  = @(git branch 2>$null | ForEach-Object { $_.Trim() -replace '^\* ', '' })
        $remote = @(git branch -r 2>$null | Where-Object { $_ -notmatch ' -> ' } |
            ForEach-Object { $_.Trim() -replace '^[^/]+/', '' })
        $branch = ($local + $remote) | Sort-Object -Unique | fzf --prompt="branch> "
        if ($branch) { git checkout $branch }
    }
}
