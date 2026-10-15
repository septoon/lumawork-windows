#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run this synthetic smoke only on a disposable GitHub Actions Windows runner.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repoRoot = Split-Path $PSScriptRoot -Parent
$portable = Join-Path $repoRoot 'artifacts/portable'
$innerPath = Join-Path $portable 'app/Инженер Lite.exe'
$server = $null
$root = $null
$previousLuma = $env:LUMAWORK_API_URL
$previousSo = $env:SIMPLEONE_API_URL

function Find-Ui([string]$value, [bool]$byId = $false) {
    if (-not $script:root) { return $null }
    $property = [System.Windows.Automation.AutomationElement]::NameProperty
    if ($byId) { $property = [System.Windows.Automation.AutomationElement]::AutomationIdProperty }
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $value)
    return $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-Ui([string]$value, [bool]$byId = $false) {
    for ($i = 0; $i -lt 60; $i++) {
        $element = Find-Ui $value $byId
        if ($element -and $element.Current.IsEnabled) { return $element }
        Start-Sleep -Milliseconds 250
    }
    throw "UI element missing or disabled: $value"
}
function Invoke-Ui([string]$value, [bool]$byId = $false) {
    $element = Wait-Ui $value $byId
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}
function Set-Ui([string]$id, [string]$value) {
    $element = Wait-Ui $id $true
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
}
function Wait-Text([string]$fragment) {
    for ($i = 0; $i -lt 60; $i++) {
        $all = $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $all) { if ($element.Current.Name.Contains($fragment)) { return } }
        Start-Sleep -Milliseconds 250
    }
    throw "UI text missing: $fragment"
}

try {
    $portProbe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $portProbe.Start(); $port = $portProbe.LocalEndpoint.Port; $portProbe.Stop()
    $env:LUMAWORK_API_URL = "http://127.0.0.1:$port"
    $env:SIMPLEONE_API_URL = "http://127.0.0.1:$port/v1"
    $server = Start-Process python -ArgumentList @('scripts/workspace-smoke-server.py', "$port") -WorkingDirectory $repoRoot -PassThru -WindowStyle Hidden
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        try { $null = Invoke-RestMethod "$env:LUMAWORK_API_URL/health"; $ready = $true; break } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'Synthetic loopback API failed to start.' }
    $null = Start-Process (Join-Path $portable 'Инженер Lite.exe') -WorkingDirectory $portable -PassThru
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $application = Get-Process -Name 'Инженер Lite' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $innerPath -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($application) { $script:root = [System.Windows.Automation.AutomationElement]::FromHandle($application.MainWindowHandle); break }
    }
    if (-not $script:root) { throw 'Application window not found.' }
    Set-Ui 'EmailBox' 'smoke@example.invalid'
    Invoke-Ui 'LoginButton' $true
    Set-Ui 'CodeBox' '000000'
    Invoke-Ui 'LoginButton' $true
    Wait-Text 'Workspace Smoke Engineer'
    Wait-Text 'Smoke Route Address'
    $null = Wait-Ui 'Импорт ГСМ XLSX'
    Invoke-Ui 'Сформировать и отправить отчёт'
    Invoke-Ui 'Сформировать и отправить'
    Wait-Text 'Smoke.xlsx отправлен'
    Write-Output 'PASS: authenticated workspace loaded profile/routes, encrypted cache persisted, confirmed report POST completed through the loopback API.'

    $navigation = Wait-Ui 'Заявки'
    $selection = $navigation.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $selection.Select()
    $null = Wait-Ui 'Импорт заявок XLSX'
    Invoke-Ui 'Войти в SimpleOne'
    Set-Ui 'SoUsername' 'smoke'
    Set-Ui 'SoPassword' 'synthetic-password'
    Invoke-Ui 'Подключиться'
    Wait-Text 'SMOKE-CLOSED'
    Wait-Text 'Smoke Closed Address'
    if (Find-Ui 'SMOKE-OPEN') { throw 'Open request leaked into the closed archive.' }
    Invoke-Ui 'Обновить'
    Wait-Text 'Smoke Closed Address'
    Write-Output 'PASS: separate SimpleOne login loaded closed addresses, excluded an open request, and manual-import/refresh controls are available.'
} catch {
    if ($script:root) {
        $all = $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $all) { if ($element.Current.Name) { Write-Output ("UI: " + $element.Current.ControlType.ProgrammaticName + " | " + $element.Current.Name) } }
    }
    try { Write-Output ((Invoke-RestMethod "$env:LUMAWORK_API_URL/smoke-status") | ConvertTo-Json -Compress -Depth 5) } catch {}
    throw
} finally {
    Get-Process -Name 'Инженер Lite' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $innerPath -or $_.Path -eq (Join-Path $portable 'Инженер Lite.exe') } | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
    $env:LUMAWORK_API_URL = $previousLuma
    $env:SIMPLEONE_API_URL = $previousSo
}
