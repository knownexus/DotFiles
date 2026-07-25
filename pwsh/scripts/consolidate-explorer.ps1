#Requires -Version 7.0
<#
.SYNOPSIS
    Consolidate all open File Explorer windows — including ALL tabs — into one window.

    Mouse clicks are blocked during the process to prevent accidental input.
    Press Ctrl+C at any time to cancel and restore input.
#>

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (-not ([System.Management.Automation.PSTypeName]'Win32').Type) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
}

if (-not ([System.Management.Automation.PSTypeName]'MouseBlocker').Type) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class MouseBlocker {
    private const int WH_MOUSE_LL      = 14;
    private const int WM_LBUTTONDOWN   = 0x0201;
    private const int WM_RBUTTONDOWN   = 0x0204;
    private const int WM_MBUTTONDOWN   = 0x0207;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_QUIT         = 0x0012;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]   private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]   private static extern bool   UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]   private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")]   private static extern bool   GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]   private static extern bool   TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]   private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]   private static extern bool   PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint   GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    private static IntPtr   _hookId   = IntPtr.Zero;
    private static uint     _threadId = 0;
    private static HookProc _proc;
    private static Thread   _thread;

    private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            var m = (int)wParam;
            if (m == WM_LBUTTONDOWN || m == WM_RBUTTONDOWN ||
                m == WM_MBUTTONDOWN || m == WM_LBUTTONDBLCLK)
                return new IntPtr(1);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
    public static void Start() {
        _proc   = Callback;
        _thread = new Thread(() => {
            _threadId = GetCurrentThreadId();
            _hookId   = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0)) {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
        Thread.Sleep(80);
    }
    public static void Stop() {
        if (_threadId != 0) {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread?.Join(500);
            _threadId = 0;
        }
    }
}
"@
}

# ── UIAutomation conditions ───────────────────────────────────────────────────

$explorerCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, "CabinetWClass"
)
$tabItemCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::TabItem
)

# ── Helpers ───────────────────────────────────────────────────────────────────

function Wait-TabCount {
    param([System.Windows.Automation.AutomationElement]$Window, [int]$Count, [int]$TimeoutMs = 2000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if ($Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabItemCond).Count -ge $Count) { return $true }
        Start-Sleep -Milliseconds 20
    }
    return $false
}

function Wait-Path {
    param($Shell, [IntPtr]$TargetHwnd, [string]$Path, [int]$TimeoutMs = 3000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $hit = $Shell.Windows() | Where-Object {
            $_.FullName -like '*explorer.exe' -and [IntPtr]$_.HWND -eq $TargetHwnd
        } | Where-Object {
            try { $_.Document.Folder.Self.Path -eq $Path } catch { $false }
        } | Select-Object -First 1
        if ($hit) { return $true }
        Start-Sleep -Milliseconds 20
    }
    return $false
}

# Navigate the active tab to $Path via the address bar using UIAutomation ValuePattern.
function Set-ExplorerAddress {
    param([System.Windows.Automation.AutomationElement]$Window, [string]$Path)

    [System.Windows.Forms.SendKeys]::SendWait("%d")

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $addressBar = $null
    while ($sw.ElapsedMilliseconds -lt 1000) {
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($focused -and $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
            $addressBar = $focused
            break
        }
        Start-Sleep -Milliseconds 20
    }

    if ($addressBar) {
        try {
            $vp = $addressBar.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if (-not $vp.Current.IsReadOnly) {
                $vp.SetValue($Path)
                [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
                return
            }
        } catch {}
    }

    # Fallback: clipboard paste.
    [System.Windows.Forms.Clipboard]::SetText($Path)
    [System.Windows.Forms.SendKeys]::SendWait("^a^v{ENTER}")
}

# ── Collection — single COM call, returns every tab across all windows ────────

$shell       = New-Object -ComObject Shell.Application
$allWins     = @($shell.Windows() | Where-Object { $_.FullName -like '*explorer.exe' })

if ($allWins.Count -le 1) {
    [System.Windows.Forms.MessageBox]::Show("Only $($allWins.Count) Explorer tab(s) open — nothing to consolidate.")
    exit
}

# Group by HWND to identify distinct windows and their tab counts.
$byHwnd      = $allWins | Group-Object { $_.HWND }
$windowCount = $byHwnd.Count

if ($windowCount -le 1) {
    [System.Windows.Forms.MessageBox]::Show("All tabs are already in one window.")
    exit
}

# Target = the window with the most tabs (used as the surviving window).
$targetGroup  = $byHwnd | Sort-Object Count -Descending | Select-Object -First 1
$targetHwnd   = [IntPtr]($targetGroup.Group[0].HWND)

# All paths across every window, sorted by the final directory name — this is the final tab order.
$allPaths     = @(
    $allWins |
        ForEach-Object {
            $path = $_.Document.Folder.Self.Path
            $name = $_.Document.Folder.Self.Name
            if ($path) { [PSCustomObject]@{ Path = $path; Name = $name } }
        } |
        Sort-Object Name |
        ForEach-Object { $_.Path }
)

$sourceWindowCount = ($byHwnd | Where-Object { [IntPtr]($_.Group[0].HWND) -ne $targetHwnd }).Count

if ($allPaths.Count -eq 0) {
    [System.Windows.Forms.MessageBox]::Show("No navigable tabs found.")
    exit
}

$result = [System.Windows.Forms.MessageBox]::Show(
    "Consolidating and sorting $($allPaths.Count) tab(s) from $($byHwnd.Count) window(s).`n`nMouse clicks will be blocked during the process.`nPress Ctrl+C to cancel.`n`nProceed?",
    "Consolidate Explorer", "YesNo"
)
if ($result -ne "Yes") { exit }

# ── Block mouse input — always restored via finally ───────────────────────────

[MouseBlocker]::Start()

try {

    $targetElement = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $explorerCond
    ) | Where-Object { $_.Current.NativeWindowHandle -eq [int]$targetHwnd } | Select-Object -First 1

    # ── Step 1: close all source windows (Ctrl+W per tab, no confirmation dialog) ──
    $byHwnd |
        Where-Object { [IntPtr]($_.Group[0].HWND) -ne $targetHwnd } |
        ForEach-Object {
            $srcHwnd  = [IntPtr]($_.Group[0].HWND)
            $tabCount = $_.Count
            [Win32]::ShowWindow($srcHwnd, 9) | Out-Null
            [Win32]::SetForegroundWindow($srcHwnd) | Out-Null
            Start-Sleep -Milliseconds 30
            for ($i = 0; $i -lt $tabCount; $i++) {
                [System.Windows.Forms.SendKeys]::SendWait("^w")
                Start-Sleep -Milliseconds 30
            }
        }

    # ── Step 2: close all but one tab in the target window ────────────────────────
    [Win32]::ShowWindow($targetHwnd, 9) | Out-Null
    [Win32]::SetForegroundWindow($targetHwnd) | Out-Null
    Start-Sleep -Milliseconds 40

    $targetTabCount = $targetGroup.Count
    for ($i = 1; $i -lt $targetTabCount; $i++) {
        [System.Windows.Forms.SendKeys]::SendWait("^w")
        Start-Sleep -Milliseconds 30
    }
    Start-Sleep -Milliseconds 40

    # ── Step 3: navigate the surviving tab to allPaths[0] ─────────────────────────
    [Win32]::SetForegroundWindow($targetHwnd) | Out-Null
    Set-ExplorerAddress -Window $targetElement -Path $allPaths[0]
    Wait-Path -Shell $shell -TargetHwnd $targetHwnd -Path $allPaths[0] | Out-Null

    # ── Step 4: open a new tab for each remaining path in sorted order ────────────
    $currentCount = 1
    foreach ($path in $allPaths[1..($allPaths.Count - 1)]) {
        [Win32]::SetForegroundWindow($targetHwnd) | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait("^t")
        $currentCount++
        Wait-TabCount -Window $targetElement -Count $currentCount | Out-Null
        Set-ExplorerAddress -Window $targetElement -Path $path
        Wait-Path -Shell $shell -TargetHwnd $targetHwnd -Path $path | Out-Null
    }

    [Win32]::SetForegroundWindow($targetHwnd) | Out-Null

} finally {
    [MouseBlocker]::Stop()
}

[System.Windows.Forms.MessageBox]::Show("Done! $($allPaths.Count) tab(s) consolidated and sorted.")
