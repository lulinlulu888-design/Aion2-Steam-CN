param(
    [Parameter(Mandatory = $true)][string]$English,
    [Parameter(Mandatory = $true)][string]$Payload,
    [Parameter(Mandatory = $true)][string]$OriginalPak,
    [Parameter(Mandatory = $true)][string]$Oodle,
    [string]$Reference = (Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\compatibility\reference.gz'),
    [string]$Repak = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'repak\repak.exe')
)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskDirectory = Join-Path $taskRepo 'bin\compatibility-tests'
New-Item -ItemType Directory -Force -Path (Join-Path $taskDirectory 'dependencies') | Out-Null
Copy-Item -LiteralPath $Oodle -Destination (Join-Path $taskDirectory 'dependencies\oo2core_9_win64.dll')
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskRunner = Join-Path $taskDirectory 'CompatibilityTests.exe'
& $taskCompiler /nologo /target:exe /main:CompatibilityTests /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/resource:$Reference,Aion2CNTool.Compatibility.Reference.gz" "/resource:$Repak,Aion2CNTool.Compatibility.Repak.exe" "/out:$taskRunner" (Join-Path $taskRepo 'Aion2CNTool.cs') (Join-Path $taskRepo 'Compatibility.cs') (Join-Path $taskRepo 'Updater.cs') (Join-Path $PSScriptRoot 'CompatibilityTests.cs')
if ($LASTEXITCODE -ne 0) { throw '兼容性测试编译失败。' }
& $taskRunner (Resolve-Path -LiteralPath $English).Path (Resolve-Path -LiteralPath $Payload).Path (Resolve-Path -LiteralPath $OriginalPak).Path
if ($LASTEXITCODE -ne 0) { throw '兼容性测试失败。' }
