param(
    [Parameter(Mandatory = $true)][string]$OriginalPak,
    [Parameter(Mandatory = $true)][string]$Payload
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ('Aion2CN-FileOps-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRoot | Out-Null
New-Item -ItemType File -Path (Join-Path $taskRoot 'isolated-test.marker') | Out-Null
$pakRelative = 'Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak'
$testPak = Join-Path $taskRoot $pakRelative
New-Item -ItemType Directory -Path (Split-Path -Parent $testPak) -Force | Out-Null
Copy-Item -LiteralPath $OriginalPak -Destination $testPak
$debugAssembly = Join-Path $repo 'bin\fileops-tests\Aion2-Steam-CN.exe'
& (Join-Path $repo 'build.ps1') -Payload $Payload -Output $debugAssembly -TestBuild
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $repo 'bin\fileops-tests\FileOpsTests.exe'
& $compiler /nologo /target:exe "/out:$runner" (Join-Path $PSScriptRoot 'FileOpsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'File operations runner compilation failed' }
& $runner $debugAssembly $taskRoot
if ($LASTEXITCODE -ne 0) { throw "File operations tests failed: $taskRoot" }
if ((Get-FileHash -LiteralPath $testPak).Hash -ne (Get-FileHash -LiteralPath $OriginalPak).Hash) {
    throw 'Restored package differs from the original'
}
Write-Output "Preserved isolated test artifacts: $taskRoot"
