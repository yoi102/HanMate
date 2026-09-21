param(
    [Parameter(Mandatory=$true)][int]$TargetProcessId,
    [Parameter(Mandatory=$true)][string]$ExpectedExecutablePath,
    [Parameter(Mandatory=$true)][ValidateSet('path','save','open','cancel','dismiss-error')][string]$Action,
    [string]$Value
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class HanMatePickerNative {
 [DllImport("user32.dll", EntryPoint="SendMessageW")] public static extern IntPtr SendMessageValue(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, string text);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
'@
$app = Get-Process -Id $TargetProcessId
if ($app.Path -ne (Resolve-Path -LiteralPath $ExpectedExecutablePath).ProviderPath -or [IO.Path]::GetFileName($app.Path) -ne 'HanMate.App.exe' -or !$app.MainWindowHandle) { throw 'Unexpected app process.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
if ($root.Current.ProcessId -ne $TargetProcessId) { throw 'Wrong app window owner.' }
# FileSavePicker is brokered. Only accept the system picker that UIA places under this exact app window.
$dialogs = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Window)) |
    Where-Object { $_.Current.Name -in 'Save As','Open' -and (Get-Process -Id $_.Current.ProcessId).Path -eq "$env:WINDIR\System32\PickerHost.exe" -and
        ($null -ne $_.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'FileNameControlHost')) -or
         $null -ne $_.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'1148'))) })
if ($dialogs.Count -ne 1) { throw 'Expected one observed system picker under HanMate.' }
$dialog = $dialogs[0]
$nodes = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
$id = switch ($Action) { path { '1001' } cancel { '2' } dismiss-error { 'CommandButton_1' } default { '1' } }
if ($Action -eq 'path' -and $dialog.Current.Name -eq 'Open') { $id = '1148' }
$nativeClass = if ($Action -eq 'path') { 'Edit' } else { 'Button' }
$matches = @($nodes | Where-Object {
    $c=$_.Current
    if ($c.AutomationId -ne $id -or !$c.NativeWindowHandle -or !$c.IsEnabled -or $c.IsOffscreen) { return $false }
    $class=[Text.StringBuilder]::new(128)
    [void][HanMatePickerNative]::GetClassName([IntPtr]$c.NativeWindowHandle,$class,128)
    $class.ToString() -eq $nativeClass
})
if ($matches.Count -ne 1) { throw "Expected one visible native $nativeClass in the observed picker." }
$target = $matches[0].Current
[uint32]$owner = 0
[void][HanMatePickerNative]::GetWindowThreadProcessId([IntPtr]$target.NativeWindowHandle,[ref]$owner)
if ($owner -ne $dialog.Current.ProcessId) { throw 'Picker control owner changed.' }
if ($Action -eq 'path') {
    if (!$Value -or ![IO.Path]::IsPathFullyQualified($Value)) { throw 'Expected an absolute test file path.' }
    # Edit replacement raises the edit-change notification; WM_SETTEXT alone can leave PickerHost's cached name stale.
    [void][HanMatePickerNative]::SendMessageValue([IntPtr]$target.NativeWindowHandle,0x00B1,[IntPtr]::Zero,[IntPtr](-1))
    [void][HanMatePickerNative]::SendMessage([IntPtr]$target.NativeWindowHandle,0x00C2,[IntPtr](1),[IO.Path]::GetFullPath($Value))
} else {
    if (($Action -eq 'save' -and $dialog.Current.Name -ne 'Save As') -or ($Action -eq 'open' -and $dialog.Current.Name -ne 'Open')) { throw 'Picker purpose mismatch.' }
    # Synchronous UIA Invoke can reenter WinRT's picker completion and fail with COM input-sync errors.
    if (![HanMatePickerNative]::PostMessage([IntPtr]$target.NativeWindowHandle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)) { throw 'Cannot post picker button action.' }
}
[pscustomobject]@{ action=$Action; dialog=$dialog.Current.Name; appPid=$TargetProcessId; pickerPid=$owner; status='dispatched' } | ConvertTo-Json -Compress
