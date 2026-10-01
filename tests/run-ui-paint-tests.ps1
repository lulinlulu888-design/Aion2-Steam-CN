param(
    [Parameter(Mandatory = $true)]
    [string]$Fixture,
    [string]$Snapshot
)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskRunner = Join-Path $taskRepo 'bin\UiPaintTests.exe'
& $taskCompiler /nologo /target:exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$taskRunner" (Join-Path $PSScriptRoot 'UiPaintTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI 测试编译失败。' }
if ($Snapshot) {
    & $taskRunner (Resolve-Path -LiteralPath $Fixture).Path $Snapshot
} else {
    & $taskRunner (Resolve-Path -LiteralPath $Fixture).Path
}
if ($LASTEXITCODE -ne 0) { throw 'UI 绘制回归测试失败。' }
