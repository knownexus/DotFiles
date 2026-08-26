# Tab completion -- mirrors zsh 50-completions

# PSReadLine is already imported by 30-settings.ps1 — check the loaded-module
# list (fast) rather than re-scanning disk with -ListAvailable.
if (Get-Module -Name PSReadLine) {

    # Menu-style completion (mirrors zsh list-suffixes / group completion)
    Set-PSReadLineKeyHandler -Key Tab       -Function MenuComplete
    Set-PSReadLineKeyHandler -Key Shift+Tab -Function MenuComplete

    # Show tooltips in the completion menu
    Set-PSReadLineOption -ShowToolTips

    # Use command history as a prediction source (requires PSReadLine 2.1+)
    $rlVersion = (Get-Module PSReadLine).Version
    if ($rlVersion -ge [version]'2.1') {
        Set-PSReadLineOption -PredictionSource History
        Set-PSReadLineOption -PredictionViewStyle InlineView
    }

    # ---------------------------------------------------------------------------
    # Inline signature predictor — requires PowerShell 7.2+ and PSReadLine 2.2+.
    # Built from <this repo>\pwsh\sig-predictor\ — run once to compile:
    #   dotnet build <this repo>\pwsh\sig-predictor
    # ---------------------------------------------------------------------------
    if ($PSVersionTable.PSVersion -ge [version]'7.2' -and $rlVersion -ge [version]'2.2') {
        $sigDll = Join-Path $script:RESDIR 'sig-predictor\bin\Release\net10.0\SignatureHintPredictor.dll'
        if ((Test-Path $sigDll) -and
            -not ([System.Management.Automation.PSTypeName]'SignatureHintPredictor').Type) {
            try {
                # Shadow-copy so the build output is never locked by a running PS session.
                # Include the PID so concurrent sessions each get their own copy.
                $tmpDll = [System.IO.Path]::Combine(
                    [System.IO.Path]::GetTempPath(),
                    "SignatureHintPredictor_$([System.Diagnostics.Process]::GetCurrentProcess().Id).dll"
                )
                Copy-Item $sigDll $tmpDll -Force -ErrorAction Stop
                Add-Type -Path $tmpDll -ErrorAction Stop
                [System.Management.Automation.Subsystem.SubsystemManager]::RegisterSubsystem(
                    [System.Management.Automation.Subsystem.SubsystemKind]::CommandPredictor,
                    [SignatureHintPredictor]::new()
                )
                Set-PSReadLineOption -PredictionSource HistoryAndPlugin
            }
            catch {
                Write-Warning "SignatureHintPredictor: failed to load — $_"
            }
        }
    }
}

# Rebuild the signature predictor and reload the profile to pick up changes
function global:build-predictor {
    dotnet build (Join-Path $script:RESDIR 'sig-predictor') -c Release --nologo -v q
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Build succeeded — reloading profile...' -ForegroundColor Green
        . $PROFILE
    }
    else {
        Write-Warning 'Build failed — profile not reloaded.'
    }
}

# posh-git gives rich `git <TAB>` completion but costs ~200ms+ to import, so
# defer it until the first git completion attempt instead of every launch.
# Importing it registers its own native completer for `git`, which replaces
# this stub — so results only appear from the second Tab press onward.
if (Get-Module -ListAvailable -Name posh-git -ErrorAction SilentlyContinue) {
    Register-ArgumentCompleter -CommandName git -Native -ScriptBlock {
        param($wordToComplete, $commandAst, $cursorPosition)
        Import-Module posh-git
    }
}

# Worktree name completion for wt-go, wt-done, wt-done-f
# Completes the last path segment of each worktree (e.g. "feature\ABC-123-foo" → "ABC-123-foo")
$script:WtCompleter = {
    param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
    git worktree list --porcelain 2>$null |
        Where-Object { $_ -match '^worktree ' } |
        ForEach-Object { $_ -replace '^worktree ', '' } |
        Where-Object { $_ -notmatch '\.git$' } |  # skip bare repo entry
        ForEach-Object { Split-Path $_ -Leaf } |
        Where-Object { $_ -like "$wordToComplete*" } |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
}

Register-ArgumentCompleter -CommandName wt-go     -ParameterName Name -ScriptBlock $script:WtCompleter
Register-ArgumentCompleter -CommandName wt-done   -ParameterName Name -ScriptBlock $script:WtCompleter
Register-ArgumentCompleter -CommandName wt-done-f -ParameterName Name -ScriptBlock $script:WtCompleter

# ---------------------------------------------------------------------------
# Branch-name completers for git alias functions
# ---------------------------------------------------------------------------

# Local branches only — for operations that only make sense on local branches.
$script:LocalBranchCompleter = {
    param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
    git branch 2>$null |
        ForEach-Object { $_.Trim() -replace '^\* ', '' } |
        Where-Object { $_ -like "$wordToComplete*" } |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
}

# All branches — local plus remote (origin/ prefix stripped, deduped) for checkout.
$script:AllBranchCompleter = {
    param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
    $local = git branch 2>$null |
        ForEach-Object { $_.Trim() -replace '^\* ', '' }
    $remote = git branch -r 2>$null |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -notmatch ' -> ' } |
        ForEach-Object { $_ -replace '^[^/]+/', '' }
    ($local + $remote) |
        Sort-Object -Unique |
        Where-Object { $_ -like "$wordToComplete*" } |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
}

# Remote branch names only — for pushdr (the target name on origin).
$script:RemoteBranchCompleter = {
    param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
    git branch -r 2>$null |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -notmatch ' -> ' } |
        ForEach-Object { $_ -replace '^[^/]+/', '' } |
        Where-Object { $_ -like "$wordToComplete*" } |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
}

# gitc / gitb use @args (no named param) — completer fires for any argument position.
Register-ArgumentCompleter -CommandName gitc -ScriptBlock $script:AllBranchCompleter
Register-ArgumentCompleter -CommandName gitb -ScriptBlock $script:LocalBranchCompleter

# gri / grim have a named -Target param.
Register-ArgumentCompleter -CommandName gri  -ParameterName Target -ScriptBlock $script:LocalBranchCompleter
Register-ArgumentCompleter -CommandName grim -ParameterName Target -ScriptBlock $script:LocalBranchCompleter

# pushdr has a named -RemoteBranch param.
Register-ArgumentCompleter -CommandName pushdr -ParameterName RemoteBranch -ScriptBlock $script:RemoteBranchCompleter

# wt-feature / wt-fix have a named -Base param.
Register-ArgumentCompleter -CommandName wt-feature -ParameterName Base -ScriptBlock $script:AllBranchCompleter
Register-ArgumentCompleter -CommandName wt-fix     -ParameterName Base -ScriptBlock $script:AllBranchCompleter

# Reload a completion/argument-completer (mirrors zsh compreload)
function global:compreload {
    param([Parameter(Mandatory)][string]$Command)
    Write-Host "Re-import the module that registers completions for '$Command' to reload them."
}
