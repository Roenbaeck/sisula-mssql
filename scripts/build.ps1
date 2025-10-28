param(
  [string]$FrameworkDir = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319",
  [string]$OutDir = "..\bin",
  [string]$NewtonsoftPath = "..\lib\Newtonsoft.Json.dll"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $NewtonsoftPath)) {
  Write-Error "Newtonsoft.Json.dll not found at $NewtonsoftPath. Place it there or pass -NewtonsoftPath."
}

$null = New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$csc = Join-Path $FrameworkDir "csc.exe"
$src = "..\clr\SisulaRenderer.cs"
$out = Join-Path $OutDir "SisulaRenderer.dll"

& $csc /nologo /target:library /out:$out `
  /r:"$NewtonsoftPath" `
  /r:"System.dll" /r:"System.Core.dll" /r:"System.Data.dll" `
  $src

Write-Host "Built: $out"