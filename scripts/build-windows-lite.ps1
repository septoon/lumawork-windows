#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$UpdateLocks,
    [switch]$Portable
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw 'WinUI/XAML build требует Windows. На Mac используйте scripts/test-core.sh.'
}

function Invoke-Native {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable завершился с кодом $LASTEXITCODE."
    }
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactsPath = Join-Path $repoRoot 'artifacts'
$testResultsPath = Join-Path $artifactsPath 'tests'
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
$msbuildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
if ($msbuildCommand) {
    $msbuildPath = $msbuildCommand.Source
} else {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswherePath)) {
        throw 'Не найден MSBuild. Требуются Visual Studio 2026 Build Tools и Windows SDK.'
    }
    $msbuildPath = & $vswherePath -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuildPath) {
        throw 'Не найден MSBuild Visual Studio. Установите Windows/WinUI build components.'
    }
}

$lockedRestore = if ($UpdateLocks) { 'false' } else { 'true' }
New-Item -ItemType Directory -Force -Path $testResultsPath | Out-Null
Push-Location $repoRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Invoke-Native $dotnetPath @('--version')
    Invoke-Native $msbuildPath @('-version', '-nologo')
    Invoke-Native $dotnetPath @(
        'test', 'tests/Engineer.Core.Tests/Engineer.Core.Tests.csproj', '-c', 'Release',
        "-p:RestoreLockedMode=$lockedRestore", '--logger', 'trx;LogFileName=core.trx',
        '--results-directory', $testResultsPath
    )
    $nativeTarget = if ($Portable) { 'Publish' } else { 'Build' }
    $nativeArguments = @(
        'src/Engineer.Lite.Windows/Engineer.Lite.Windows.csproj', '/restore', "/t:$nativeTarget",
        "/p:Configuration=$Configuration", '/p:Platform=x64', "/p:RestoreLockedMode=$lockedRestore",
        '/p:AppxPackageSigningEnabled=false', '/p:GenerateAppxPackageOnBuild=false',
        '/p:ContinuousIntegrationBuild=true', "/bl:$artifactsPath/windows-lite-build.binlog", '/nologo'
    )
    if ($Portable) {
        $nativeArguments += @('/p:WindowsPackageType=None', '/p:EnableMsixTooling=false')
    }
    Invoke-Native $msbuildPath $nativeArguments
    $outputPath = Join-Path $repoRoot "src/Engineer.Lite.Windows/bin/x64/$Configuration/net10.0-windows10.0.26100.0/win-x64"
    if ($Portable) {
        $outputPath = Join-Path $outputPath 'publish'
    }
    if (-not (Test-Path (Join-Path $outputPath 'Engineer.Lite.Windows.exe'))) {
        throw 'Build завершился, но ожидаемый WinUI executable отсутствует.'
    }
    $artifactOutputPath = Join-Path $artifactsPath 'windows-lite'
    if (Test-Path $artifactOutputPath) {
        Remove-Item -Path $artifactOutputPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $artifactOutputPath | Out-Null
    Copy-Item -Path (Join-Path $outputPath '*') -Destination $artifactOutputPath -Recurse -Force
} finally {
    Pop-Location
}
