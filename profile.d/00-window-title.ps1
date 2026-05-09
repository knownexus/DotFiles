# Terminal window-title stack — mirrors zsh 00-entitle

$script:TitleStack = [System.Collections.Generic.List[string]]::new()
$script:TitleStack.Add("WARNING: HIT ROCK BOTTOM")

function global:Set-WindowTitle {
    param([string]$Title)
    $Host.UI.RawUI.WindowTitle = $Title
}

function global:Push-Title {
    param([string]$Title)
    $script:TitleStack.Add($Title)
    Set-WindowTitle $script:TitleStack[-1]
}

function global:Pop-Title {
    if ($script:TitleStack.Count -gt 1) {
        $script:TitleStack.RemoveAt($script:TitleStack.Count - 1)
    }
    Set-WindowTitle $script:TitleStack[-1]
}

# Run a block with a title pushed, popping on exit
function global:Invoke-Entitled {
    param(
        [string]$Title,
        [scriptblock]$Action
    )
    Push-Title $Title
    try { & $Action }
    finally { Pop-Title }
}
