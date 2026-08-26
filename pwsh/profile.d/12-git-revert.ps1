# Git revert — detect and clean up changes that were later reverted

# Find changes made in commit A that were undone in commit B.
# Usage: grev <commit-a> <commit-b>
function global:grev {
    param(
        [Parameter(Mandatory, Position = 0)][string]$CommitA,
        [Parameter(Mandatory, Position = 1)][string]$CommitB
    )
    & (Join-Path $script:RESDIR 'scripts\Find-RevertedChanges.ps1') $CommitA $CommitB
}

# Rewrite commit A to remove changes that commit B later undid.
# Usage: grev-clean <commitA> <commitB> [-DryRun]
function global:grev-clean {
    param(
        [Parameter(Mandatory, Position = 0)][string]$CommitA,
        [Parameter(Mandatory, Position = 1)][string]$CommitB,
        [switch]$DryRun
    )

    $shaA   = git rev-parse $CommitA 2>$null
    $shaB   = git rev-parse $CommitB 2>$null
    $shortA = git rev-parse --short $CommitA 2>$null
    $shortB = git rev-parse --short $CommitB 2>$null
    if (-not $shaA) { Write-Error "Cannot resolve: $CommitA"; return }
    if (-not $shaB) { Write-Error "Cannot resolve: $CommitB"; return }

    if (git status --porcelain) {
        Write-Error "Working tree is not clean. Stash or commit changes first."
        return
    }

    $gitRoot = git rev-parse --show-toplevel

    function script:Get-FileLines {
        param([string]$Commit, [char]$Sign)
        $map     = @{}
        $file    = $null
        $oldLine = 0
        $newLine = 0
        git show $Commit | ForEach-Object {
            if ($_ -match '^\+\+\+ b/(.+)') {
                $file    = $Matches[1]
                $oldLine = 0
                $newLine = 0
            } elseif ($_ -match '^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@') {
                $oldLine = [int]$Matches[1]
                $newLine = [int]$Matches[2]
            } elseif ($file -and $_.Length -gt 1) {
                $ch = $_[0]
                if ($ch -eq '+' -and $_[1] -ne '+') {
                    if ($Sign -eq '+') {
                        if (-not $map.ContainsKey($file)) { $map[$file] = [System.Collections.Generic.List[object]]::new() }
                        $map[$file].Add([PSCustomObject]@{ Line = $newLine; Content = $_.Substring(1) })
                    }
                    $newLine++
                } elseif ($ch -eq '-' -and $_[1] -ne '-') {
                    if ($Sign -eq '-') {
                        if (-not $map.ContainsKey($file)) { $map[$file] = [System.Collections.Generic.List[object]]::new() }
                        $map[$file].Add([PSCustomObject]@{ Line = $oldLine; Content = $_.Substring(1) })
                    }
                    $oldLine++
                } else {
                    $oldLine++; $newLine++
                }
            }
        }
        return $map
    }

    $aAdded   = Get-FileLines -Commit $shaA -Sign '+'
    $aRemoved = Get-FileLines -Commit $shaA -Sign '-'
    $bAdded   = Get-FileLines -Commit $shaB -Sign '+'
    $bRemoved = Get-FileLines -Commit $shaB -Sign '-'

    $toStrip   = @{}
    $toRestore = @{}

    foreach ($f in $aAdded.Keys) {
        if ($bRemoved.ContainsKey($f)) {
            $bContents = $bRemoved[$f] | ForEach-Object { $_.Content }
            $overlap = @($aAdded[$f] | Where-Object { $bContents -contains $_.Content })
            if ($overlap.Count) { $toStrip[$f] = $overlap }
        }
    }
    foreach ($f in $aRemoved.Keys) {
        if ($bAdded.ContainsKey($f)) {
            $bContents = $bAdded[$f] | ForEach-Object { $_.Content }
            $overlap = @($aRemoved[$f] | Where-Object { $bContents -contains $_.Content })
            if ($overlap.Count) { $toRestore[$f] = $overlap }
        }
    }

    if (-not $toStrip.Count -and -not $toRestore.Count) {
        Write-Host "No redundant changes found between $shortA and $shortB." -ForegroundColor Cyan
        return
    }

    Write-Host ""
    Write-Host "Changes to remove from $shortA :" -ForegroundColor Yellow
    Write-Host ""
    $allFiles = @($toStrip.Keys) + @($toRestore.Keys) | Select-Object -Unique
    foreach ($f in $allFiles) {
        Write-Host "  $f" -ForegroundColor Cyan
        if ($toStrip.ContainsKey($f)) {
            $toStrip[$f] | ForEach-Object {
                Write-Host ("    - L{0,-5} {1}" -f $_.Line, $_.Content) -ForegroundColor Red
            }
        }
        if ($toRestore.ContainsKey($f)) {
            $toRestore[$f] | ForEach-Object {
                Write-Host ("    + L{0,-5} {1}" -f $_.Line, $_.Content) -ForegroundColor Green
            }
        }
    }
    Write-Host ""

    if ($DryRun) { Write-Host "Dry run — no changes made." -ForegroundColor DarkGray; return }

    $ans = Read-Host "Rewrite commit $shortA to remove these changes? (y/n)"
    if ($ans -ne 'y') { Write-Host "Aborted." -ForegroundColor Yellow; return }

    $tmpEditor = [System.IO.Path]::GetTempFileName() + '.ps1'
    @"
param([string]`$f)
`$sha = '$($shaA.Substring(0,7))'
(Get-Content `$f) -replace "^pick (`$sha\S*)(.*)", 'edit `$1`$2' | Set-Content `$f
"@ | Set-Content $tmpEditor -Encoding UTF8

    $prevSeqEditor = $env:GIT_SEQUENCE_EDITOR
    $env:GIT_SEQUENCE_EDITOR = "pwsh -NoProfile -File `"$tmpEditor`""

    try {
        git rebase -i "$($shaA)^"

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Rebase failed — run 'git rebase --abort' to recover."
            return
        }

        foreach ($f in $allFiles) {
            $fullPath = Join-Path $gitRoot $f
            if (-not (Test-Path $fullPath)) {
                Write-Warning "File not found in working tree: $f — skipping"
                continue
            }

            $lines = [System.Collections.Generic.List[string]]::new(
                [System.IO.File]::ReadAllLines($fullPath))

            if ($toStrip.ContainsKey($f)) {
                $stripContents = $toStrip[$f] | ForEach-Object { $_.Content }
                for ($i = $lines.Count - 1; $i -ge 0; $i--) {
                    if ($stripContents -contains $lines[$i].TrimEnd()) { $lines.RemoveAt($i) }
                }
            }

            if ($toRestore.ContainsKey($f)) {
                $restoreContents = $toRestore[$f] | ForEach-Object { $_.Content }
                $parentRaw  = git show "HEAD^:$f" 2>$null
                if ($parentRaw) {
                    $parent  = [string[]]$parentRaw
                    $current = $lines.ToArray()
                    $result  = [System.Collections.Generic.List[string]]::new()
                    $pi      = 0
                    foreach ($cl in $current) {
                        while ($pi -lt $parent.Length -and $parent[$pi].TrimEnd() -ne $cl.TrimEnd()) {
                            if ($restoreContents -contains $parent[$pi].TrimEnd()) { $result.Add($parent[$pi]) }
                            $pi++
                        }
                        $result.Add($cl)
                        if ($pi -lt $parent.Length) { $pi++ }
                    }
                    while ($pi -lt $parent.Length) {
                        if ($restoreContents -contains $parent[$pi].TrimEnd()) { $result.Add($parent[$pi]) }
                        $pi++
                    }
                    $lines = $result
                } else {
                    Write-Warning "Could not read parent state of $f — removals not restored"
                }
            }

            [System.IO.File]::WriteAllLines($fullPath, $lines)
            git add $f
        }

        git commit --amend --no-edit

        if ($LASTEXITCODE -ne 0) {
            # Empty-amend is the common case here: if B undid everything A did,
            # stripping it leaves nothing to commit relative to A's parent.
            git diff --cached --quiet HEAD^ 2>$null
            if ($LASTEXITCODE -eq 0) {
                Write-Host ""
                Write-Warning "$shortA's changes are now fully cancelled out by $shortB — amending would leave it empty."
                Write-Host "Run one of:" -ForegroundColor DarkGray
                Write-Host "  git commit --amend --allow-empty --no-edit; git rebase --continue   # keep an empty marker commit" -ForegroundColor DarkGray
                Write-Host "  git rebase --skip                                                    # drop $shortA entirely" -ForegroundColor DarkGray
                Write-Host "  git rebase --abort                                                   # give up, restore original history" -ForegroundColor DarkGray
            } else {
                Write-Error "Amend failed — run 'git rebase --abort' to recover."
            }
            return
        }

        $prevGitEditor  = $env:GIT_EDITOR
        $env:GIT_EDITOR = 'true'
        git rebase --continue
        $continueExit   = $LASTEXITCODE
        $env:GIT_EDITOR = $prevGitEditor

        if ($continueExit -ne 0) {
            $conflicted    = @(git diff --name-only --diff-filter=U 2>$null)
            $toAutoResolve = @($conflicted | Where-Object { $allFiles -contains $_ })
            $unexpected    = @($conflicted | Where-Object { $allFiles -notcontains $_ })

            if ($unexpected.Count -gt 0) {
                Write-Host ""
                Write-Warning "Unexpected conflicts in: $($unexpected -join ', '). Resolve manually then run 'git rebase --continue'."
                return
            }

            if ($toAutoResolve.Count -eq 0) {
                Write-Host ""
                Write-Warning "Rebase --continue failed with no detectable conflicts. Run 'git rebase --abort' to recover."
                return
            }

            foreach ($cf in $toAutoResolve) {
                git checkout --theirs -- $cf
                git add $cf
            }

            $env:GIT_EDITOR = 'true'
            git rebase --continue
            $env:GIT_EDITOR = $prevGitEditor

            if ($LASTEXITCODE -ne 0) {
                Write-Host ""
                Write-Warning "Rebase still has conflicts after auto-resolution. Resolve manually then run 'git rebase --continue'."
                return
            }
        }

        Write-Host ""
        Write-Host "Done — commit $shortA has been cleaned up." -ForegroundColor Green

    } finally {
        $env:GIT_SEQUENCE_EDITOR = $prevSeqEditor
        Remove-Item $tmpEditor -ErrorAction SilentlyContinue
    }
}

# Scan a commit range for changes that were later reverted in a non-adjacent commit.
# Usage: grev-scan [<range>]
function global:grev-scan {
    param([Parameter(Position = 0)][string]$Range)

    if (-not (Assert-GitRepo)) { return }

    if (-not $Range) {
        $base = git merge-base HEAD develop 2>$null
        $Range = if ($base) { "$base..HEAD" } else { 'HEAD~20..HEAD' }
    }

    Write-Host "Scanning $Range for redundant changes..." -ForegroundColor DarkGray

    $minLen     = 10
    $commitData = [System.Collections.Generic.List[object]]::new()
    $current    = $null
    $file       = $null
    $oldLine    = 0
    $newLine    = 0

    git log -p --format="COMMIT:%H:%ad:%s" --date=short --reverse $Range 2>$null | ForEach-Object {
        if ($_ -match '^COMMIT:([^:]+):([^:]+):(.*)') {
            if ($current) { $commitData.Add($current) }
            $current = [PSCustomObject]@{
                Sha     = $Matches[1]
                Date    = $Matches[2]
                Subject = $Matches[3]
                Added   = @{}
                Removed = @{}
            }
            $file    = $null
            $oldLine = 0
            $newLine = 0
        } elseif ($_ -match '^\+\+\+ b/(.+)') {
            $file    = $Matches[1]
            $oldLine = 0
            $newLine = 0
        } elseif ($_ -match '^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@') {
            $oldLine = [int]$Matches[1]
            $newLine = [int]$Matches[2]
        } elseif ($current -and $file -and $_.Length -gt 1) {
            $ch = $_[0]
            if ($ch -eq '+' -and $_[1] -ne '+') {
                $content = $_.Substring(1).TrimEnd()
                if ($content.Trim().Length -ge $minLen) {
                    if (-not $current.Added.ContainsKey($file)) {
                        $current.Added[$file] = [System.Collections.Generic.List[object]]::new()
                    }
                    $current.Added[$file].Add([PSCustomObject]@{ Line = $newLine; Content = $content })
                }
                $newLine++
            } elseif ($ch -eq '-' -and $_[1] -ne '-') {
                $content = $_.Substring(1).TrimEnd()
                if ($content.Trim().Length -ge $minLen) {
                    if (-not $current.Removed.ContainsKey($file)) {
                        $current.Removed[$file] = [System.Collections.Generic.List[object]]::new()
                    }
                    $current.Removed[$file].Add([PSCustomObject]@{ Line = $oldLine; Content = $content })
                }
                $oldLine++
            } else {
                $oldLine++
                $newLine++
            }
        }
    }
    if ($current) { $commitData.Add($current) }

    if ($commitData.Count -eq 0) {
        Write-Host "No commits found in range '$Range'." -ForegroundColor Yellow
        return
    }

    $lineIndex = @{}

    for ($i = 0; $i -lt $commitData.Count; $i++) {
        $c = $commitData[$i]
        foreach ($f in $c.Added.Keys) {
            foreach ($entry in $c.Added[$f]) {
                $key = "$f|$($entry.Content)"
                if (-not $lineIndex.ContainsKey($key)) {
                    $lineIndex[$key] = [System.Collections.Generic.List[object]]::new()
                }
                $lineIndex[$key].Add([PSCustomObject]@{ Idx = $i; Sign = '+'; Line = $entry.Line })
            }
        }
        foreach ($f in $c.Removed.Keys) {
            foreach ($entry in $c.Removed[$f]) {
                $key = "$f|$($entry.Content)"
                if (-not $lineIndex.ContainsKey($key)) {
                    $lineIndex[$key] = [System.Collections.Generic.List[object]]::new()
                }
                $lineIndex[$key].Add([PSCustomObject]@{ Idx = $i; Sign = '-'; Line = $entry.Line })
            }
        }
    }

    $findings = [System.Collections.Generic.List[object]]::new()

    foreach ($entry in $lineIndex.GetEnumerator()) {
        $keyParts = $entry.Key -split '\|', 2
        $f        = $keyParts[0]
        $content  = $keyParts[1]
        $events   = @($entry.Value | Sort-Object Idx)

        for ($i = 0; $i -lt $events.Count; $i++) {
            $sign     = $events[$i].Sign
            $opposite = if ($sign -eq '+') { '-' } else { '+' }
            for ($j = $i + 1; $j -lt $events.Count; $j++) {
                if ($events[$j].Sign -eq $opposite -and $events[$j].Idx -ne $events[$i].Idx) {
                    $findings.Add([PSCustomObject]@{
                        File     = $f
                        Content  = $content
                        Type     = if ($sign -eq '+') { 'AddedThenReverted' } else { 'RemovedThenRestored' }
                        OrigIdx  = $events[$i].Idx
                        OrigLine = $events[$i].Line
                        RevIdx   = $events[$j].Idx
                        RevLine  = $events[$j].Line
                    })
                    break
                }
            }
        }
    }

    if ($findings.Count -eq 0) {
        Write-Host "No redundant changes found in $Range." -ForegroundColor Cyan
        return
    }

    $grouped = $findings |
        Group-Object { "$($_.OrigIdx)|$($_.RevIdx)" } |
        Sort-Object { [int]($_.Name -split '\|')[0] }

    Write-Host ""
    Write-Host "Redundant changes in $Range :" -ForegroundColor Yellow

    foreach ($group in $grouped) {
        $sample = $group.Group[0]
        $orig   = $commitData[$sample.OrigIdx]
        $rev    = $commitData[$sample.RevIdx]

        Write-Host ""
        Write-Host "  Original : $($orig.Sha.Substring(0,8))  $($orig.Date)  $($orig.Subject)" -ForegroundColor DarkYellow
        Write-Host "  Reverted : $($rev.Sha.Substring(0,8))  $($rev.Date)  $($rev.Subject)"   -ForegroundColor DarkYellow
        Write-Host ""

        $group.Group | Group-Object File | ForEach-Object {
            Write-Host "    $($_.Name)" -ForegroundColor Cyan
            $_.Group | Sort-Object OrigLine | ForEach-Object {
                if ($_.Type -eq 'AddedThenReverted') {
                    Write-Host ("      - L{0,-5} {1}" -f $_.OrigLine, $_.Content) -ForegroundColor Red
                } else {
                    Write-Host ("      + L{0,-5} {1}" -f $_.OrigLine, $_.Content) -ForegroundColor Green
                }
            }
        }
    }

    Write-Host ""
    Write-Host "Tip: grev-clean <original-sha> <revert-sha> to strip redundant changes from the original commit." -ForegroundColor DarkGray
}
