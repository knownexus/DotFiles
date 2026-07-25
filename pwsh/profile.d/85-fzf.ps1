# PSFzf integration — inert until the PSFzf module is installed. Mirrors
# the zsh side's fzf integration (Ctrl+T/Ctrl+R bindings, wt-gof, gitcf).
#   Install-Module PSFzf -Scope CurrentUser
if (Get-Module -ListAvailable -Name PSFzf) {
    Import-Module PSFzf -ErrorAction SilentlyContinue
    Set-PsFzfOption -PSReadlineChordProvider 'Ctrl+t' -PSReadlineChordReverseHistory 'Ctrl+r'

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
