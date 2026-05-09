# Environment variables — mirrors zsh 70-variables

if (-not $env:COMPUTERNAME)  { $env:COMPUTERNAME = hostname }
if (-not $env:VISUAL)        { $env:VISUAL = 'vim' }
if (-not $env:EDITOR)        { $env:EDITOR = $env:VISUAL }
