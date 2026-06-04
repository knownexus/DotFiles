# Environment variables — mirrors zsh 70-variables

if (-not $env:COMPUTERNAME)  { $env:COMPUTERNAME = hostname }
if (-not $env:VISUAL)        { $env:VISUAL = 'vim' }
if (-not $env:EDITOR)        { $env:EDITOR = $env:VISUAL }

# ---------------------------------------------------------------------------
# Repo root alias — displayed in the prompt instead of the full path.
#
# Example:  $global:RepoRoot   = 'C:\repos'
#           $global:RepoSymbol = '#'
# Prompt will show:  #\DriveFurtherAPI\develop  instead of  C:\repos\DriveFurtherAPI\develop
# Set either to $null to disable.
# ---------------------------------------------------------------------------
$global:RepoRoot   = 'C:\repos'
$global:RepoSymbol = '#'
