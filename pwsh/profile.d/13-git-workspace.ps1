# Workspace Git UI. Loading the profile only defines functions; builds and scans
# happen on demand. This deliberately does not shadow git or change $PWD.
$script:GwsSource = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\GitWorkspace'))

function global:Get-GitWorkspaceBuildDirectory {
    param([switch]$Tests)
    # Source-specific output lets an older UI keep running while an update builds.
    $gwsFiles = @(Get-ChildItem -LiteralPath $script:GwsSource -File |
        Where-Object { $_.Extension -in '.cs', '.csproj' -or $_.Name -eq 'NuGet.Config' })
    if ($Tests) {
        $gwsFiles += @(Get-ChildItem -LiteralPath (Join-Path $script:GwsSource '..\GitWorkspace.Tests') -File |
            Where-Object { $_.Extension -in '.cs', '.csproj' })
    }
    $gwsSignature = ($gwsFiles | Sort-Object FullName | ForEach-Object {
        $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }) -join "`n"
    $gwsHasher = [System.Security.Cryptography.SHA256]::Create()
    try { $gwsBuildId = [BitConverter]::ToString($gwsHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($gwsSignature))).Replace('-', '').Substring(0, 20).ToLowerInvariant() }
    finally { $gwsHasher.Dispose() }
    $gwsKind = if ($Tests) { 'tests' } else { 'releases' }
    Join-Path $script:GwsSource "bin\$gwsKind\$gwsBuildId"
}

function global:Build-GitWorkspace {
    param([string]$OutputDirectory)
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Git Workspace requires the .NET 10 SDK. Install it, then run gws again.'
    }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Get-GitWorkspaceBuildDirectory }
    & dotnet build (Join-Path $script:GwsSource 'GitWorkspace.csproj') -c Release --output $OutputDirectory --nologo --configfile (Join-Path $script:GwsSource 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Git Workspace build failed. See the build output above.' }
    Set-Content -LiteralPath (Join-Path $OutputDirectory '.ready') -Value 'Build completed.'
}

function global:gws {
    # No param() binder: preserve native flags, quoted messages and paths intact.
    if ($args.Count -gt 0 -and $args[0] -eq 'build') { Build-GitWorkspace; return }
    if ($args.Count -gt 0 -and $args[0] -eq 'test') {
        $testProject = Join-Path $script:GwsSource '..\GitWorkspace.Tests\GitWorkspace.Tests.csproj'
        $testOutput = Get-GitWorkspaceBuildDirectory -Tests
        & dotnet build $testProject -c Release --output $testOutput --nologo --configfile (Join-Path $script:GwsSource 'NuGet.Config')
        if ($LASTEXITCODE -eq 0) {
            & dotnet (Join-Path $testOutput 'GitWorkspace.Tests.dll')
        }
        return
    }
    $buildOutput = Get-GitWorkspaceBuildDirectory
    $dll = Join-Path $buildOutput 'gws.dll'
    if (-not (Test-Path -LiteralPath $dll) -or -not (Test-Path -LiteralPath (Join-Path $buildOutput '.ready'))) {
        Build-GitWorkspace -OutputDirectory $buildOutput
    }
    & dotnet $dll @args
}

Register-ArgumentCompleter -CommandName gws -ScriptBlock {
    param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
    'status', 'add', 'build', 'test', '--demo', '--snapshot', '--help', '--config', '--json', '--root' |
        Where-Object { $_ -like "$wordToComplete*" } |
        ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }
}
