# Git branch — branch, checkout, stash, cherry-pick, clean

# Branch
function global:gitb { git branch @args }

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

# Clean working tree (untracked files and dirs)
function global:gclean { git clean -fd @args }
