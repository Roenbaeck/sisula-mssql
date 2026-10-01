<#
    Runs the shared Sisula conformance fixtures against the renderer, without SQL Server.

    The fixtures live in the sisula repository (tests/fixtures/*.json), which is the source of truth
    for the language. They are language-neutral: { name, template, bindings, expected } or
    { name, template, bindings, error }. This script compiles clr/SisulaRenderer.cs with
    SISULA_TEST defined, together with tests/SqlJsonEmulation.cs, which stands in for the T-SQL JSON
    functions, and tests/FixtureRunner.cs, then runs every fixture through the public
    fn_sisulate entry point.

    Usage: .\tests\run-fixtures.ps1 [-Fixtures <directory>] [-Filter <text in a fixture name>]
           .\tests\run-fixtures.ps1 -WriteSqlTest sql\test_fixtures.sql

    -WriteSqlTest does not run anything. It writes the same fixtures as a T-SQL script that runs
    each one through dbo.fn_sisulate on a real server, which is the check of the T-SQL side.

    Without -Fixtures it uses tests\fixtures of the sisula checkout next to this repository.
    Uses the same .NET Framework 4 csc.exe as scripts\build.ps1, so it compiles C# 5.

    This checks the renderer's own logic. The emulation reproduces the documented behaviour of
    JSON_VALUE, JSON_QUERY and OPENJSON; the scripts in sql\ run the same kind of cases on a real
    server, and are the check of the T-SQL side.
#>
[CmdletBinding()]
param(
    [string] $Fixtures,
    [string] $Filter,
    [string] $WriteSqlTest,
    [string] $FrameworkDir = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$parent = Split-Path -Parent $root

if (-not $Fixtures) {
    $Fixtures = Join-Path $parent 'sisula\tests\fixtures'
    if (-not (Test-Path $Fixtures)) { throw "No sisula checkout with tests\fixtures found at $Fixtures; clone the sisula repository next to this one, or pass -Fixtures." }
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
if ($WriteSqlTest) {
    # Not a test run: write the fixtures as a T-SQL script to run on a real server.
    & $exe --sql $Fixtures $WriteSqlTest
    exit $LASTEXITCODE
}
$arguments = @($Fixtures)
if ($Filter) { $arguments += $Filter }
& $exe @arguments
exit $LASTEXITCODE
