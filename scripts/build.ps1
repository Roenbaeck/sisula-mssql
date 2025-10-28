param(
  [string]$FrameworkDir = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319",
  [string]$OutDir = "..\\bin"
)

$ErrorActionPreference = "Stop"

$buildStart = Get-Date

# Resolve repo root relative to this script, to avoid dependence on current working directory
$ScriptRoot = $PSScriptRoot
if (-not $ScriptRoot) { $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path }
$RepoRoot = (Resolve-Path (Join-Path $ScriptRoot "..")).Path

# Normalize OutDir to absolute path relative to the script folder
if (-not (Split-Path -IsAbsolute $OutDir)) { $OutDir = Join-Path $ScriptRoot $OutDir }

$null = New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Resolve csc.exe (try provided FrameworkDir, then Framework64 fallback)
$csc = Join-Path $FrameworkDir "csc.exe"
if (!(Test-Path $csc)) {
  $fw64 = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
  $csc64 = Join-Path $fw64 "csc.exe"
  if (Test-Path $csc64) { $csc = $csc64 }
}
if (!(Test-Path $csc)) {
  throw "csc.exe not found. Checked: '$FrameworkDir' and Framework64 fallback. Pass -FrameworkDir or install .NET Framework 4.x Dev Pack."
}

$src = Join-Path $RepoRoot "clr\SisulaRenderer.cs"
$out = Join-Path $OutDir "SisulaRenderer.dll"

if (!(Test-Path $src)) {
  throw "Source file not found: $src"
}

$logPath = Join-Path $OutDir "build.csc.log"

$args = @(
  "/nologo",
  "/target:library",
  "/out:$out",
  "/r:System.dll",
  "/r:System.Core.dll",
  "/r:System.Data.dll",
  $src
)

Write-Host "Compiling with: $csc $($args -join ' ')"

# Write header to log (overwrite)
$outAbs = (Resolve-Path $OutDir).Path
$srcAbs = (Resolve-Path $src).Path
# Sanitize to relative paths to avoid embedding user profile paths
function Make-Relative($base, $path) {
  if ($path.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) { return ".\" + $path.Substring($base.Length).TrimStart('\\') }
  return $path
}
$outRel = Make-Relative $RepoRoot $outAbs
$srcRel = Make-Relative $RepoRoot $srcAbs
$argsSan = ($args -join ' ').Replace($RepoRoot, '.')

$header = @(
  "=== csc build ===",
  "Start: $(Get-Date -Format o)",
  "CSC: $csc",
  "OutDir: $outRel",
  "Src: $srcRel",
  "Args: $argsSan"
)
$header | Out-File -FilePath $logPath -Encoding UTF8

$output = & $csc @args 2>&1
$exitCode = $LASTEXITCODE
if ($null -eq $output -or ($output -is [array] -and $output.Length -eq 0) -or ($output -isnot [array] -and [string]::IsNullOrWhiteSpace([string]$output))) {
  Add-Content -Path $logPath -Value "[no compiler output]"
} else {
  $output | Add-Content -Path $logPath
}

# Footer
$duration = (Get-Date) - $buildStart
$footer = @(
  "ExitCode: $exitCode",
  "DurationMs: $([int]$duration.TotalMilliseconds)",
  "End: $(Get-Date -Format o)",
  ("Result: " + ($(if ($exitCode -eq 0) { 'SUCCESS' } else { 'FAIL' })))
)
$footer | Add-Content -Path $logPath

if ($exitCode -ne 0) {
  Write-Error "csc failed with exit code $exitCode. See log: $logPath"
}

Write-Host "Built: $out in $([int]$duration.TotalMilliseconds) ms"
Write-Host "Log: $logPath"