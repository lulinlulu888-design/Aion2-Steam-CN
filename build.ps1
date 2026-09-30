param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$Payload,

    [string]$Output = (Join-Path $PSScriptRoot 'bin\Aion2-Steam-CN.exe')
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到 .NET Framework 64 位 C# 编译器。'
}

$outputDirectory = Split-Path -Parent $Output
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    "/win32manifest:$PSScriptRoot\app.manifest" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    "/resource:$Payload,Aion2CNTool.Payload.L10NString.dat" `
    "/out:$Output" `
    "$PSScriptRoot\Aion2CNTool.cs"

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码：$LASTEXITCODE"
}

Get-Item -LiteralPath $Output
Get-FileHash -Algorithm SHA256 -LiteralPath $Output
