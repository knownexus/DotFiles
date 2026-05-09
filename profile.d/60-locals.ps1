# Local PATH configuration — mirrors zsh 60-locals

function script:Add-PathEntry {
    param([string]$Dir)
    if ((Test-Path $Dir) -and $env:PATH -notlike "*$Dir*") {
        $env:PATH = "$Dir;$env:PATH"
    }
}

# Personal executables
Add-PathEntry "$HOME\bin"
Add-PathEntry "$HOME\.local\bin"

# Rust (cargo)
Add-PathEntry "$HOME\.cargo\bin"

# Haskell (cabal)
Add-PathEntry "$HOME\AppData\Roaming\cabal\bin"

# Go
Add-PathEntry "$HOME\go\bin"

# Android SDK platform tools
Add-PathEntry "$HOME\AppData\Local\Android\Sdk\platform-tools"

# Node version managers (nvm / fnm / volta)
Add-PathEntry "$HOME\.volta\bin"
Add-PathEntry "$HOME\AppData\Roaming\fnm"

Remove-Item Function:Add-PathEntry -ErrorAction SilentlyContinue
