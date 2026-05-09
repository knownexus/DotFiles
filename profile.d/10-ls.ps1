# ls aliases — mirrors zsh 10-ls

function global:l {
    Get-ChildItem @args | Format-Wide Name -AutoSize
}

function global:ll {
    Get-ChildItem @args | Format-Table Mode, LastWriteTime, Length, Name -AutoSize
}

function global:la {
    Get-ChildItem -Force @args | Format-Wide Name -AutoSize
}

function global:lla {
    Get-ChildItem -Force @args | Format-Table Mode, LastWriteTime, Length, Name -AutoSize
}
