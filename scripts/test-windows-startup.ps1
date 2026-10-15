#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repoRoot = Split-Path $PSScriptRoot -Parent
$portable = Join-Path $repoRoot 'artifacts/portable'
$innerPath = Join-Path $portable 'app/Инженер Lite.exe'
$launcher = Join-Path $portable 'Инженер Lite.exe'
$application = $null
try {
    $started = Start-Process -FilePath $launcher -WorkingDirectory $portable -PassThru
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 500
        $application = Get-Process -Name 'Инженер Lite' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $innerPath -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if (-not $application) { continue }
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($application.MainWindowHandle)
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Получить код')
        $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($button -and $button.Current.IsEnabled) {
            Write-Output 'PASS: root launcher opened the WinUI LumaWork login screen; request-code button is visible and enabled.'
            exit 0
        }
    }
    throw 'Startup smoke failed: no enabled request-code button on the login screen.'
} finally {
    Get-Process -Name 'Инженер Lite' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $innerPath -or $_.Path -eq $launcher } | Stop-Process -Force -ErrorAction SilentlyContinue
}
