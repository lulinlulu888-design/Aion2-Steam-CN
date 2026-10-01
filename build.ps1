param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$Payload,

    [string]$Output = (Join-Path $PSScriptRoot 'bin\Aion2-Steam-CN.exe'),

    [switch]$TestBuild
)

$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Aion2CNTool.cs') -Raw
$expectedPayloadHash = [regex]::Match($source, 'const string PayloadHash = "([A-F0-9]{64})";').Groups[1].Value
if (-not $expectedPayloadHash -or (Get-FileHash -Algorithm SHA256 -LiteralPath $Payload).Hash -ne $expectedPayloadHash) {
    throw '语言载荷与源码 PayloadHash 不匹配，已停止打包。请先完成载荷复核并同步版本、哈希。'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到 .NET Framework 64 位 C# 编译器。'
}

$outputDirectory = Split-Path -Parent $Output
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$compilerArguments = @(
    '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
    "/win32manifest:$PSScriptRoot\app.manifest",
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Web.Extensions.dll',
    "/resource:$Payload,Aion2CNTool.Payload.L10NString.dat",
    "/resource:$PSScriptRoot\assets\aion2cn-logo.png,Aion2CNTool.Assets.Logo.png",
    "/out:$Output",
    "$PSScriptRoot\Aion2CNTool.cs",
    "$PSScriptRoot\Updater.cs"
)
if ($TestBuild) { $compilerArguments += '/define:DEBUG' }
& $compiler $compilerArguments

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码：$LASTEXITCODE"
}

Get-Item -LiteralPath $Output
Get-FileHash -Algorithm SHA256 -LiteralPath $Output
