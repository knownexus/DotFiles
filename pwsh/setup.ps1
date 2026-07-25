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

# Copy vim dotfiles
cp C:\repos\pwsh\configs\.vimrc ~/.vimrc
cp C:\repos\pwsh\configs\.vim ~/.vim/
cp C:\repos\pwsh\configs\cheatsheet.txt ~/.vim/cheatsheet.txt

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