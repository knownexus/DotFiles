# direnv integration — inert until direnv is installed. Mirrors the zsh
# side's direnv integration (per-project environment variables auto-loaded
# from a .envrc file).
$direnvExe = (Get-Command direnv -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1).Source
if ($direnvExe) {
    # `direnv hook` output is static per direnv.exe — spawning it on every
    # launch costs ~90ms for no benefit, so cache the generated script and
    # only regenerate when the exe itself changes.
    $cacheFile = Join-Path $env:TEMP 'direnv-hook-pwsh.ps1'
    $exeStamp  = (Get-Item $direnvExe).LastWriteTimeUtc.Ticks
    $stampFile = "$cacheFile.stamp"
    $stale = -not (Test-Path $cacheFile) -or
             -not (Test-Path $stampFile) -or
             (Get-Content $stampFile -ErrorAction SilentlyContinue) -ne "$exeStamp"
    if ($stale) {
        direnv hook pwsh | Out-File -FilePath $cacheFile -Encoding utf8
        Set-Content -Path $stampFile -Value "$exeStamp"
    }
    . $cacheFile
}
