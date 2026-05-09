# SSH wrappers with title-stack integration -- mirrors zsh 20-ssh
# Requires 00-window-title.ps1 to be loaded first.

$script:_sshCmd = Get-Command ssh -CommandType Application -ErrorAction SilentlyContinue
$script:SshExe  = if ($script:_sshCmd) { $script:_sshCmd.Source } else { $null }

if ($script:SshExe) {

    # ssh wrapper that pushes a window title while connected
    function global:ssh {
        param([Parameter(ValueFromRemainingArguments)][string[]]$SshArgs)
        Push-Title "[ssh] $SshArgs [ssh]"
        try { & $script:SshExe @SshArgs }
        finally { Pop-Title }
    }

    # ssh -X (X11 forwarding) with title
    function global:ssx {
        param([Parameter(ValueFromRemainingArguments)][string[]]$SshArgs)
        Push-Title "[ssh-X] $SshArgs [ssh-X]"
        try { & $script:SshExe -X @SshArgs }
        finally { Pop-Title }
    }

}
