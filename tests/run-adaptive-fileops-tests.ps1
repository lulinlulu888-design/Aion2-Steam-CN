param(
    [Parameter(Mandatory = $true)][string]$CurrentSourceDat,
    [Parameter(Mandatory = $true)][string]$Payload,
    [Parameter(Mandatory = $true)][string]$OldPak,
    [Parameter(Mandatory = $true)][string]$Oodle,
    [string]$Repak = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'repak\repak.exe')
)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ('Aion2CN-Adaptive-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRoot | Out-Null
New-Item -ItemType File -Path (Join-Path $taskRoot 'isolated-test.marker') | Out-Null
$taskSource = Join-Path $taskRoot 'fixture\AION2\Content\L10N\Text\en-US\L10NString.dat'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskSource) | Out-Null
Copy-Item -LiteralPath $CurrentSourceDat -Destination $taskSource
$taskPak = Join-Path $taskRoot 'Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskPak) | Out-Null
& $Repak pack (Join-Path $taskRoot 'fixture') $taskPak --version V11
if ($LASTEXITCODE -ne 0) { throw 'Fixture PAK creation failed' }
$taskDat = Join-Path $taskRoot 'Aion2\Content\L10N\Text\en-US\L10NString.dat'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskDat) | Out-Null
Copy-Item -LiteralPath $Payload -Destination $taskDat
Copy-Item -LiteralPath $OldPak -Destination ($taskPak + '.aion2cn.v2.backup')
$taskOldHash = (Get-FileHash -LiteralPath $OldPak).Hash
$taskPayloadHash = (Get-FileHash -LiteralPath $Payload).Hash
$taskState = "format=2`r`nstatus=installed`r`ngame_build=steam-25650019-global-152629`r`nhad_dat=False`r`npre_pak_hash=$taskOldHash`r`npayload_hash=$taskPayloadHash`r`n"
# Test fixture metadata, not a project source edit.
[IO.File]::WriteAllText((Join-Path (Split-Path -Parent $taskDat) 'Aion2CNTool.state'), $taskState)
$taskDebug = Join-Path $taskRepo 'bin\adaptive-tests\Aion2-Steam-CN.exe'
& (Join-Path $taskRepo 'build.ps1') -Payload $Payload -Output $taskDebug -TestBuild
New-Item -ItemType Directory -Force -Path (Join-Path (Split-Path -Parent $taskDebug) 'dependencies') | Out-Null
Copy-Item -LiteralPath $Oodle -Destination (Join-Path (Split-Path -Parent $taskDebug) 'dependencies\oo2core_9_win64.dll')
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskRunner = Join-Path $taskRepo 'bin\adaptive-tests\AdaptiveFileOpsTests.exe'
& $taskCompiler /nologo /target:exe "/out:$taskRunner" (Join-Path $PSScriptRoot 'AdaptiveFileOpsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Adaptive test runner compilation failed' }
& $taskRunner $taskDebug $taskRoot
if ($LASTEXITCODE -ne 0) { throw "Adaptive file operations failed: $taskRoot" }
Write-Output "Preserved isolated test artifacts: $taskRoot"
