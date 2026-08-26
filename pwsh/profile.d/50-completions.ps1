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
        if ($rlVersion -ge [version]'2.2') {
            # ListView groups suggestions by source under a labelled header
            # (History first, then each plugin, e.g. SignatureHints) -- live,
            # in-terminal, non-modal, exactly like InlineView but showing all
            # candidates instead of just one. Its per-row rendering can't
            # preserve a plugin-embedded custom color per item (tried and
            # confirmed: ListView's highlighting pass overwrites it), so
            # suggestions render in PSReadLine's normal ListPrediction color
            # -- distinguishable by section header, not by color. Requires
            # 2.2+, same as the plugin predictor below; older PSReadLine
            # falls back to InlineView.
            Set-PSReadLineOption -PredictionViewStyle ListView
        } else {
            Set-PSReadLineOption -PredictionViewStyle InlineView
        }
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

                # Cycle History-only -> SignatureHints-only -> both (Ctrl+Spacebar)
                # -- useful when the two sources' suggestions for the same
                # prefix crowd each other out in the list.
                function global:Switch-PredictionSource {
                    $current = (Get-PSReadLineOption).PredictionSource
                    if ($current -eq [Microsoft.PowerShell.PredictionSource]::History) {
                        Set-PSReadLineOption -PredictionSource Plugin
                        # Window title, not Write-Host -- printing a line (even
                        # with a leading `n to move to a fresh one) permanently
                        # pushes the current input row down each time this is
                        # pressed. Overwritten by the next full prompt redraw
                        # (global:prompt in 40-prompt.ps1 sets it every render).
                        Set-WindowTitle "pwsh: predictions = SignatureHints only"
                    } elseif ($current -eq [Microsoft.PowerShell.PredictionSource]::Plugin) {
                        Set-PSReadLineOption -PredictionSource HistoryAndPlugin
                        Set-WindowTitle "pwsh: predictions = History + SignatureHints"
                    } else {
                        Set-PSReadLineOption -PredictionSource History
                        Set-WindowTitle "pwsh: predictions = History only"
                    }
                    # Changing PredictionSource alone doesn't refresh a list
                    # that's already on screen -- PSReadLine only recomputes
                    # predictions on the next real buffer edit. Insert a space
                    # then delete it (net zero change, any cursor position) to
                    # force that recompute under the new source immediately
                    # instead of requiring the user to retype the command.
                    [Microsoft.PowerShell.PSConsoleReadLine]::Insert(' ')
                    [Microsoft.PowerShell.PSConsoleReadLine]::BackwardDeleteChar()
                }
                Set-PSReadLineKeyHandler -Key Ctrl+Spacebar -ScriptBlock {
                    param($key, $arg)
                    Switch-PredictionSource
                }
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
