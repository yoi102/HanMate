param(
    [Parameter(Mandatory=$true)][int]$TargetProcessId,
    [ValidateSet('snapshot','screenshot','invoke','reveal','toggle','expand','collapse','select','select-parent','focus','value','key','click','right-click','stroke')][string]$Action = 'snapshot',
    [string]$Id,
    [string]$Name,
    [string]$Value,
    [string]$ExpectedExecutablePath,
    [string]$OutputDirectory = "$env:TEMP/HanMate-windows-ui"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = Get-Process -Id $TargetProcessId
$expected = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../HanMate.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/HanMate.App.exe'))
if ($ExpectedExecutablePath) {
    $expected = (Resolve-Path -LiteralPath $ExpectedExecutablePath -ErrorAction Stop).ProviderPath
    if ([IO.Path]::GetFileName($expected) -ne 'HanMate.App.exe') { throw 'Expected a HanMate executable.' }
}
if ($process.Path -ne $expected -or $process.MainWindowHandle -eq 0) { throw 'Expected the workspace HanMate Release process and its window.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if ($root.Current.ProcessId -ne $TargetProcessId) { throw 'Window owner mismatch.' }
$nodes = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
if ($Action -notin @('snapshot','screenshot')) {
    if (!$Id -and !$Name) { throw 'An exact observed ID or name is required.' }
    $matches = @($nodes | Where-Object { ($Action -eq 'reveal' -or ! $_.Current.IsOffscreen) -and (!$Id -or $_.Current.AutomationId -eq $Id) -and (!$Name -or $_.Current.Name -eq $Name) } |
        Group-Object -Property { $_.GetRuntimeId() -join ':' } | ForEach-Object { $_.Group[0] })
    if ($matches.Count -ne 1) { throw "Expected one target; found $($matches.Count)." }
    $target = $matches[0]
    if ($target.Current.ProcessId -ne $TargetProcessId -or !$target.Current.IsEnabled -or ($target.Current.IsOffscreen -and $Action -ne 'reveal')) { throw 'Target is not an enabled visible control owned by HanMate.' }
    if ($Action -eq 'select-parent') {
        $target = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($target)
        if ($target.Current.ProcessId -ne $TargetProcessId -or $target.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) { throw 'Expected an owned parent list item.' }
    }
    switch ($Action) {
        { $_ -in 'click','right-click','stroke' } {
            Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HanMateMouse {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; }
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@
            [void][HanMateMouse]::SetForegroundWindow($process.MainWindowHandle)
            $bounds = $target.Current.BoundingRectangle
            $point = New-Object HanMateMouse+Point
            $point.X = [int]($bounds.X + $bounds.Width / 2); $point.Y = [int]($bounds.Y + $bounds.Height / 2)
            [uint32]$owner = 0
            [void][HanMateMouse]::GetWindowThreadProcessId([HanMateMouse]::WindowFromPoint($point), [ref]$owner)
            if ($owner -ne $TargetProcessId -or [HanMateMouse]::GetForegroundWindow() -ne $process.MainWindowHandle) { throw 'Click point or foreground window is not owned by HanMate.' }
            if ($Action -eq 'stroke') {
                if ($Id -ne 'Handwriting.Pad') { throw 'Stroke requires the observed handwriting pad.' }
                $strokePoints = @($Value | ConvertFrom-Json)
                if ($strokePoints.Count -lt 2 -or $strokePoints.Count -gt 64) { throw 'Expected 2-64 stroke points.' }
                $side = [Math]::Min($bounds.Width, $bounds.Height) - 16
                if ($side -le 0) { throw 'Pad has no writable area.' }
                $left = $bounds.X + ($bounds.Width - $side) / 2; $top = $bounds.Y + ($bounds.Height - $side) / 2
                foreach ($pair in $strokePoints) {
                    if ($pair.Count -ne 2 -or [double]::IsNaN([double]$pair[0]) -or [double]::IsNaN([double]$pair[1]) -or $pair[0] -lt .02 -or $pair[0] -gt .98 -or $pair[1] -lt .02 -or $pair[1] -gt .98) { throw 'Stroke point outside pad.' }
                }
                try {
                    $point.X = [int]($left + $side * $strokePoints[0][0]); $point.Y = [int]($top + $side * $strokePoints[0][1])
                    [void][HanMateMouse]::GetWindowThreadProcessId([HanMateMouse]::WindowFromPoint($point), [ref]$owner)
                    if ($owner -ne $TargetProcessId) { throw 'Stroke start is not owned by HanMate.' }
                    [void][HanMateMouse]::SetCursorPos($point.X, $point.Y)
                    [HanMateMouse]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
                    for ($i=1; $i -lt $strokePoints.Count; $i++) {
                        for ($step=1; $step -le 12; $step++) {
                            if ([HanMateMouse]::GetForegroundWindow() -ne $process.MainWindowHandle) { throw 'Lost HanMate foreground during stroke.' }
                            $fraction=$step/12.0
                            $point.X=[int]($left+$side*($strokePoints[$i-1][0]*(1-$fraction)+$strokePoints[$i][0]*$fraction))
                            $point.Y=[int]($top+$side*($strokePoints[$i-1][1]*(1-$fraction)+$strokePoints[$i][1]*$fraction))
                            [void][HanMateMouse]::GetWindowThreadProcessId([HanMateMouse]::WindowFromPoint($point), [ref]$owner)
                            if ($owner -ne $TargetProcessId) { throw 'Stroke point is not owned by HanMate.' }
                            [void][HanMateMouse]::SetCursorPos($point.X,$point.Y)
                            Start-Sleep -Milliseconds 12
                        }
                    }
                } finally { [HanMateMouse]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
                break
            }
            if (![HanMateMouse]::SetCursorPos($point.X, $point.Y)) { throw 'Cannot place pointer.' }
            $down = if ($Action -eq 'click') { 2 } else { 8 }
            $up = if ($Action -eq 'click') { 4 } else { 16 }
            [HanMateMouse]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero)
            [HanMateMouse]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero)
        }
        'invoke' { ([System.Windows.Automation.InvokePattern]$target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
        'reveal' { ([System.Windows.Automation.ScrollItemPattern]$target.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern)).ScrollIntoView() }
        'toggle' { ([System.Windows.Automation.TogglePattern]$target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle() }
        'expand' { ([System.Windows.Automation.ExpandCollapsePattern]$target.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand() }
        'collapse' { ([System.Windows.Automation.ExpandCollapsePattern]$target.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Collapse() }
        'select' { ([System.Windows.Automation.SelectionItemPattern]$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
        'select-parent' { ([System.Windows.Automation.SelectionItemPattern]$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
        'focus' { $target.SetFocus() }
        'value' { ([System.Windows.Automation.ValuePattern]$target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($Value) }
        'key' {
            $keys = @{ tab='{TAB}'; enter='{ENTER}'; home='{HOME}'; end='{END}'; down='{DOWN}'; up='{UP}'; escape='{ESC}'; f10='{F10}' }
            if (!$keys.ContainsKey($Value)) { throw 'Unsupported navigation key.' }
            $target.SetFocus()
            if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $TargetProcessId) { throw 'Keyboard focus is not owned by HanMate.' }
            Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HanMateKeyboardOwner {
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
'@
            [uint32]$keyboardOwner = 0
            [void][HanMateKeyboardOwner]::GetWindowThreadProcessId([HanMateKeyboardOwner]::GetForegroundWindow(),[ref]$keyboardOwner)
            if ($keyboardOwner -ne $TargetProcessId) { throw 'HanMate is not the foreground keyboard recipient.' }
            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.SendKeys]::SendWait($keys[$Value])
        }
    }
}
# A completed action may rebuild the visual tree. Retry observation only, never the action.
for ($attempt = 0; $attempt -lt 3; $attempt++) {
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        if ($root.Current.ProcessId -ne $TargetProcessId) { throw 'Window owner changed.' }
        $nodes = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
        $rows = @($nodes | ForEach-Object {
            $c = $_.Current; $b = $c.BoundingRectangle
            [pscustomobject]@{name=$c.Name;id=$c.AutomationId;type=$c.ControlType.ProgrammaticName;enabled=$c.IsEnabled;offscreen=$c.IsOffscreen;
                focused=$c.HasKeyboardFocus;focusable=$c.IsKeyboardFocusable;pid=$c.ProcessId;x=$b.X;y=$b.Y;width=$b.Width;height=$b.Height}
        })
        break
    } catch {
        if ($attempt -eq 2 -or $_.Exception.GetBaseException() -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw }
        Start-Sleep -Milliseconds 200
    }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
if ($Action -eq 'screenshot') {
    Add-Type -AssemblyName System.Drawing
    Add-Type 'using System; using System.Runtime.InteropServices; public static class HanMateWindowCapture { [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags); }'
    $bounds = $root.Current.BoundingRectangle
    $bitmap = New-Object System.Drawing.Bitmap ([int]$bounds.Width),([int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try {
        if (![HanMateWindowCapture]::PrintWindow($process.MainWindowHandle, $dc, 2)) { throw 'Owned window capture failed.' }
    } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    try { $bitmap.Save((Join-Path $OutputDirectory "$stamp.png")) } finally { $bitmap.Dispose() }
}
$output = Join-Path $OutputDirectory "$stamp.json"
$rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $output -Encoding UTF8
Write-Output "Snapshot: $output"
$rows | Where-Object { !$_.offscreen -and ($_.name -or $_.id) } | Select-Object name,id,type,focused | ConvertTo-Json -Compress
