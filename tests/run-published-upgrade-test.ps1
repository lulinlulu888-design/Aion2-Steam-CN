param(
    [string]$OldInstaller = (Join-Path (Split-Path -Parent $PSScriptRoot) 'downloads\Aion2-Steam-CN-v2.2.0.exe'),
    [string]$ExpectedVersion = '2.2.1'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $repo 'bin\PublishedUpgradeTests.exe'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $runner) | Out-Null
& $compiler /nologo /target:exe "/out:$runner" (Join-Path $PSScriptRoot 'PublishedUpgradeTests.cs')
if ($LASTEXITCODE -ne 0) { throw '已发布旧版更新测试编译失败。' }
& $runner (Resolve-Path -LiteralPath $OldInstaller).Path $ExpectedVersion
if ($LASTEXITCODE -ne 0) { throw '已发布旧版更新测试失败，详见上方输出。' }
