# Setup script — installs Git, Claude Code, and configures dotfiles + PATH

# Install Git
winget install --id Git.Git -e --source winget

# Add Git's vim to user PATH
$gitVim = "C:\Program Files\Git\usr\bin"
[Environment]::SetEnvironmentVariable(
    "Path",
    [Environment]::GetEnvironmentVariable("Path", "User") + ";$gitVim",
    "User"
)

# Point ~/.vimrc at the shared config (see shared/vim/) via a sourcing stub,
# so editing the repo takes effect immediately instead of needing a re-copy.
$sharedVim = Join-Path (Split-Path $PSScriptRoot -Parent) 'shared\vim\vimrc'
$stub = '" Managed by dotfiles setup - edit shared/vim/vimrc, not this file.' + "`n" +
        'source ' + ($sharedVim -replace '\\', '/') + "`n"
New-Item -ItemType Directory -Force -Path ~/.vim/undodir | Out-Null
[System.IO.File]::WriteAllText(
    (Join-Path $HOME '.vimrc'), $stub, (New-Object System.Text.UTF8Encoding $false))

# Install Claude Code
irm https://claude.ai/install.ps1 | iex

# Add Claude Code to user PATH
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
[Environment]::SetEnvironmentVariable(
    "Path",
    "$userPath;C:\Users\Phillip.Smyth\.local\bin",
    "User"
)

# Assign full access perms to user for c:\repos
$acl = Get-Acl "C:\repos"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $env:USERNAME,
    "FullControl",
    "ContainerInherit,ObjectInherit",
    "None",
    "Allow"
)
$acl.SetAccessRule($rule)
Set-Acl "C:\repos" $acl