<#
    Runs the shared Sisula conformance fixtures against the renderer, without SQL Server.

    The fixtures live in the sisula repository (tests/fixtures/*.json), which is the source of truth
    for the language. They are language-neutral: { name, template, bindings, expected } or
    { name, template, bindings, error }. This script compiles clr/SisulaRenderer.cs with
    SISULA_TEST defined, together with tests/SqlJsonEmulation.cs, which stands in for the T-SQL JSON
    functions, and tests/FixtureRunner.cs, then runs every fixture through the public
    fn_sisulate entry point.

    Usage: .\tests\run-fixtures.ps1 [-Fixtures <directory>] [-Filter <text in a fixture name>]

    Without -Fixtures it looks for a sisula checkout next to this repository: sisula-master, then sisula.
    Uses the same .NET Framework 4 csc.exe as scripts\build.ps1, so it compiles C# 5.

    This checks the renderer's own logic. The emulation reproduces the documented behaviour of
    JSON_VALUE, JSON_QUERY and OPENJSON; the scripts in sql\ run the same kind of cases on a real
    server, and are the check of the T-SQL side.
#>
[CmdletBinding()]
param(
    [string] $Fixtures,
    [string] $Filter,
    [string] $FrameworkDir = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$parent = Split-Path -Parent $root

if (-not $Fixtures) {
    $candidates = @('sisula-master', 'sisula') | ForEach-Object { Join-Path $parent "$_\tests\fixtures" }
    $Fixtures = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Fixtures) { throw "No sisula checkout with tests\fixtures found next to this repository; pass -Fixtures." }
}
$Fixtures = (Resolve-Path $Fixtures).Path

$csc = Join-Path $FrameworkDir 'csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found in $FrameworkDir; pass -FrameworkDir." }

$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$exe = Join-Path $bin 'FixtureRunner.exe'

$output = & $csc /nologo /target:exe /define:SISULA_TEST "/out:$exe" /r:System.dll /r:System.Core.dll /r:System.Data.dll `
    (Join-Path $root 'clr\SisulaRenderer.cs') (Join-Path $PSScriptRoot 'SqlJsonEmulation.cs') (Join-Path $PSScriptRoot 'FixtureRunner.cs') 2>&1
if ($LASTEXITCODE -ne 0) { $output | ForEach-Object { Write-Host $_ }; throw 'The test build failed.' }

Write-Host "Fixtures: $Fixtures"
$arguments = @($Fixtures)
if ($Filter) { $arguments += $Filter }
& $exe @arguments
exit $LASTEXITCODE
