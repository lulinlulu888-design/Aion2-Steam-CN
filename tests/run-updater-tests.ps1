param([switch]$Network)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixture = Join-Path $repo 'bin\v2.2.0\Aion2-Steam-CN.exe'
if (-not (Test-Path -LiteralPath $fixture)) { throw '先构建 bin\v2.2.0\Aion2-Steam-CN.exe。' }
$runner = Join-Path $repo 'bin\UpdaterTests.exe'
$fixtureDirectory = Join-Path $repo 'bin\update-fixture'
New-Item -ItemType Directory -Force -Path $fixtureDirectory | Out-Null
& $compiler /nologo /target:winexe "/out:$fixtureDirectory\Aion2-Steam-CN.exe" (Join-Path $PSScriptRoot 'UpdateFixture.cs')
if ($LASTEXITCODE -ne 0) { throw '更新重启测试样例编译失败。' }
& $compiler /nologo /target:exe /reference:System.Web.Extensions.dll "/out:$runner" (Join-Path $repo 'Updater.cs') (Join-Path $PSScriptRoot 'UpdaterTests.cs')
if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
if ($Network) { & $runner $fixture --network } else { & $runner $fixture }
if ($LASTEXITCODE -ne 0) { throw '更新测试失败。' }
