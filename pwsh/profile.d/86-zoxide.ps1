# zoxide integration — inert until zoxide is installed. Mirrors the zsh
# side's zoxide integration (z <partial-name> to jump to a frecent
# directory). zoxide's own init script hooks in via Set-Location/
# Push-Location/Pop-Location, not by touching `prompt`, so it coexists
# cleanly with the custom prompt in 40-prompt.ps1.
if (Get-Command zoxide -ErrorAction SilentlyContinue) {
    # `zoxide init` output is static per zoxide.exe — spawning it on every
    # launch costs ~100ms for no benefit, so cache the generated script and
    # only regenerate when the exe itself changes.
    $zoxideExe   = (Get-Command zoxide).Source
    $cacheFile   = Join-Path $env:TEMP 'zoxide-init-pwsh.ps1'
    $exeStamp    = (Get-Item $zoxideExe).LastWriteTimeUtc.Ticks
    $stampFile   = "$cacheFile.stamp"
    $stale = -not (Test-Path $cacheFile) -or
             -not (Test-Path $stampFile) -or
             (Get-Content $stampFile -ErrorAction SilentlyContinue) -ne "$exeStamp"
    if ($stale) {
        zoxide init powershell | Out-File -FilePath $cacheFile -Encoding utf8
        Set-Content -Path $stampFile -Value "$exeStamp"
    }
    . $cacheFile
}
