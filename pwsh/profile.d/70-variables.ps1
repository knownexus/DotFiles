# Environment variables — mirrors zsh 70-variables

if (-not $env:COMPUTERNAME)  { $env:COMPUTERNAME = hostname }
if (-not $env:VISUAL)        { $env:VISUAL = 'vim' }
if (-not $env:EDITOR)        { $env:EDITOR = $env:VISUAL }

# $HOME is a PowerShell automatic variable, not an OS environment variable --
# it never propagates to child processes on its own. Tools that shell out
# and read the real env block (direnv's config-dir lookup, notably) need
# $env:HOME set explicitly, unlike POSIX shells which always export it.
if (-not $env:HOME)          { $env:HOME = $env:USERPROFILE }

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

# ---------------------------------------------------------------------------
# Claude API model selection — used by 13-claude.ps1.
# API key must be set as a user-level env var (never committed here):
#   [Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', 'sk-ant-...', 'User')
# ---------------------------------------------------------------------------
$global:ClaudeModel     = 'claude-sonnet-4-6'          # ai-pr, ai-review
$global:ClaudeModelFast = 'claude-haiku-4-5-20251001'  # ai-commit (fast + cheap)
