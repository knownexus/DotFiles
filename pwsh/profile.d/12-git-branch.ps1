# Git branch — branch, checkout, stash, cherry-pick, clean

# Branch — bare gitb lists each branch (newest first) with its last commit date and head; args pass through
function global:gitb {
    if ($args.Count -gt 0) { git branch @args; return }
    # Tab-separated fields; the subject is trimmed at its first ':'
    $rows = @(git branch --sort=-committerdate --format='%(HEAD)%09%(refname:short)%09%(committerdate:format:%Y-%m-%d %H:%M)%09%(objectname:short)%09%(contents:subject)' |
        ForEach-Object {
            $f = $_ -split "`t", 5
            [pscustomobject]@{ Head = $f[0]; Name = $f[1]; Date = $f[2]; Hash = $f[3]; Subject = "$(($f[4] -split ':', 2)[0])" }
        })
    if (-not $rows) { return }

    $cols = try { $Host.UI.RawUI.WindowSize.Width } catch { 0 }
    if ($cols -le 0) { $cols = 120 }
    $maxName = ($rows.Name    | Measure-Object -Property Length -Maximum).Maximum
    $maxHash = ($rows.Hash    | Measure-Object -Property Length -Maximum).Maximum
    $maxSubj = ($rows.Subject | Measure-Object -Property Length -Maximum).Maximum
    # Name column fits the longest name, shrinking (min 10) to leave room for date, hash and some subject;
    # longer names overflow it (cut with '…' only if date and hash would no longer fit), and subjects
    # are cut at the window edge so nothing wraps
    $fixed = 2 + 1 + 16 + 1 + $maxHash + 1
    $nameW = [Math]::Min($maxName, [Math]::Max(10, $cols - 1 - $fixed - [Math]::Min($maxSubj, 12)))
    $fit   = [Math]::Max(10, $cols - 1 - $fixed)
    $e = [char]27
    foreach ($r in $rows) {
        $name  = $r.Name
        if ($name.Length -gt $fit) { $name = $name.Substring(0, $fit - 1) + '…' }
        $name  = $name.PadRight($nameW)
        $avail = $cols - 1 - (2 + $name.Length + 1 + 16 + 1 + $r.Hash.Length + 1)
        $subj  = $r.Subject
        if ($subj.Length -gt $avail) { $subj = if ($avail -gt 1) { $subj.Substring(0, $avail - 1) + '…' } else { '' } }
        "$($r.Head) $e[32m$name$e[m $e[33m$($r.Date)$e[m $e[34m$($r.Hash)$e[m $subj"
    }
}

# Checkout
function global:gitc  { git checkout @args }
function global:gitcb { git checkout -b @args }
function global:gitcd { git checkout develop }

# Stash
# gp is a built-in alias — remove it so our version wins
Remove-Item -Path Alias:gp -Force -ErrorAction SilentlyContinue
function global:git-stash-pop { git stash pop }
Set-Alias -Name gp -Value git-stash-pop -Force -Option AllScope
function global:gst    { git stash push @args }
function global:gstl   { git stash list @args }
function global:stash  { git stash save @args }
function global:stashp { git stash pop }

# Cherry-pick
function global:gitcp { git cherry-pick @args }

# Clean working tree (untracked files and dirs) — confirms before deleting
function global:gclean {
    $preview = git clean -fdn @args
    if (-not $preview) {
        Write-Host "Nothing to clean."
        return
    }
    Write-Host "This will permanently delete the following untracked files/directories:" -ForegroundColor Yellow
    $preview | ForEach-Object { Write-Host $_ }
    $ans = Read-Host "Proceed with git clean -fd $($args -join ' ')? (y/n)"
    if ($ans -eq 'y') { git clean -fd @args }
}
