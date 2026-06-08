# Claude API functions — ai-commit, ai-pr, ai-review
#
# SETUP: Set your Anthropic API key as a user-level environment variable so it
# is never committed to this repo. Run this once in an admin PowerShell session:
#
#   [Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', 'sk-ant-...', 'User')
#
# Then restart your terminal. Models and other defaults are in 70-variables.ps1.

# ---------------------------------------------------------------------------
# Shared API helper
# ---------------------------------------------------------------------------

function script:Invoke-ClaudeAPI {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [string]$System,
        [string]$Model     = $global:ClaudeModel,
        [int]$MaxTokens    = 1024
    )

    $key = $env:ANTHROPIC_API_KEY
    if (-not $key) {
        Write-Error "ANTHROPIC_API_KEY is not set. See the setup instructions at the top of 13-claude.ps1."
        return $null
    }

    $body = @{
        model      = $Model
        max_tokens = $MaxTokens
        messages   = @(@{ role = 'user'; content = $Prompt })
    }
    if ($System) { $body['system'] = $System }

    try {
        $response = Invoke-RestMethod `
            -Uri     'https://api.anthropic.com/v1/messages' `
            -Method  POST `
            -Headers @{
                'x-api-key'         = $key
                'anthropic-version' = '2023-06-01'
                'content-type'      = 'application/json'
            } `
            -Body ($body | ConvertTo-Json -Depth 5)

        return $response.content[0].text
    } catch {
        Write-Error "Claude API error: $($_.Exception.Message)"
        return $null
    }
}

# ---------------------------------------------------------------------------
# ai-commit — generate a conventional commit message from the staged diff.
#   ai-commit          prints the message
#   ai-commit -Apply   prints and commits immediately
# ---------------------------------------------------------------------------

function global:ai-commit {
    param([switch]$Apply)

    $diff = git diff --staged
    if (-not $diff) {
        Write-Host "Nothing staged — run 'git add' first." -ForegroundColor Yellow
        return
    }

    $system = @'
You generate git commit messages. Output ONLY the commit message — no explanation, no markdown, no code fences.

Rules:
- Format: type(scope): short summary
- Types: feat, fix, test, docs, refactor, chore, perf, style
- Imperative mood, all lowercase, no full stop, under 72 chars
- Scope is optional — use it when the diff clearly targets one module or area
- Add a blank line and a short body only if the why is non-obvious from the summary
'@

    $prompt = "Write a conventional commit message for this staged diff:`n`n$diff"

    Write-Host "Asking Claude..." -ForegroundColor DarkGray
    $message = Invoke-ClaudeAPI -Prompt $prompt -System $system -Model $global:ClaudeModelFast -MaxTokens 256
    if (-not $message) { return }

    Write-Host ""
    Write-Host $message -ForegroundColor Cyan
    Write-Host ""

    if ($Apply) {
        git commit -m $message
    } else {
        Write-Host "Run 'ai-commit -Apply' to commit with this message." -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------
# ai-pr — generate a PR title and description from the diff of the current
# branch against a base (defaults to develop).
#   ai-pr              prints to stdout (pipeable to clip)
#   ai-pr -Base main   diff against a different base
# ---------------------------------------------------------------------------

function global:ai-pr {
    param([string]$Base = 'develop')

    $branch = git rev-parse --abbrev-ref HEAD
    $log    = git log "${Base}..HEAD" --oneline 2>$null
    $diff   = git diff "${Base}...HEAD" 2>$null

    if (-not $diff) {
        Write-Host "No diff found between '$branch' and '$Base'. Is the base branch correct?" -ForegroundColor Yellow
        return
    }

    $system = @'
You generate Azure DevOps pull request descriptions. Output ONLY the formatted PR — no preamble, no code fences.

Format exactly:
## Title
type(scope): short summary   ← conventional commit format, under 72 chars

## What
2-4 sentences describing what changed.

## Why
The business or technical motivation.

## How to test
Numbered steps to verify the change works.

## Checklist
- [ ] Tests added or updated
- [ ] No PII logged (entity IDs only)
- [ ] Responses assigned to a variable before return
- [ ] No business logic in controllers
- [ ] CancellationToken threaded through where applicable
'@

    $prompt = @"
Branch: $branch

Commits:
$log

Diff:
$diff
"@

    Write-Host "Asking Claude..." -ForegroundColor DarkGray
    $result = Invoke-ClaudeAPI -Prompt $prompt -System $system -Model $global:ClaudeModel -MaxTokens 1024
    if (-not $result) { return }

    Write-Output $result
}

# ---------------------------------------------------------------------------
# ai-review — review code or a diff against DriveFurther standards.
#   git diff HEAD~1 | ai-review
#   ai-review -File src/MyService.cs
#   git diff HEAD~1 | ai-review -Question "is the error handling correct?"
# ---------------------------------------------------------------------------

function global:ai-review {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline)][string]$InputObject,
        [string]$File,
        [string]$Question
    )

    begin {
        $lines = [System.Collections.Generic.List[string]]::new()
    }

    process {
        if ($InputObject) { $lines.Add($InputObject) }
    }

    end {
        $content = if ($File) {
            if (-not (Test-Path $File)) { Write-Error "File not found: $File"; return }
            Get-Content $File -Raw
        } elseif ($lines.Count -gt 0) {
            $lines -join "`n"
        } else {
            Write-Host "Pipe a diff or pass -File:`n  git diff HEAD~1 | ai-review`n  ai-review -File MyService.cs" -ForegroundColor Yellow
            return
        }

        $system = @'
You are a senior C# / TypeScript / Angular code reviewer. Apply these DriveFurther standards:

Architecture:
- No business logic in controllers — actions over ~15 lines belong in a MediatR handler
- CQRS: Commands return void / Result<T> / new entity ID. Queries return DTOs only, never domain entities
- Strict layer rules: Domain has no deps. Application depends on Domain only. Infrastructure on App+Domain. API on Application only

Error handling:
- All service methods and handlers return Result<T> for predictable failures
- Never swallow exceptions silently — always log and rethrow, or return a Result error
- Never return exception detail to callers — log server-side, return a safe generic message
- No try/catch in controller actions unless catching a specific type intentionally

Null safety:
- Nullable enabled — no null-forgiving operator (!) without a comment explaining why it is safe
- Use ?. ?? pattern matching or Result<T> instead of unsafe casts

Async:
- All I/O methods must be async and accept CancellationToken, threaded through the full call chain
- No .Wait() or .Result

Logging (Serilog):
- Named properties only — never string interpolation in log calls
- No PII — entity IDs only
- Correct log level for the severity

Testing:
- xUnit + FluentAssertions + NSubstitute
- AAA structure, blank-line separated
- Every bug fix needs a regression test

Style:
- Curly braces on all control flow, including single-line if
- Responses assigned to a variable before return

Be concise. Group findings by severity: MUST FIX / CONSIDER / MINOR. Skip anything clearly correct.
'@

        $prompt = if ($Question) {
            "$Question`n`n$content"
        } else {
            "Review this code:`n`n$content"
        }

        Write-Host "Asking Claude..." -ForegroundColor DarkGray
        $result = Invoke-ClaudeAPI -Prompt $prompt -System $system -Model $global:ClaudeModel -MaxTokens 2048
        if (-not $result) { return }

        Write-Output $result
    }
}
