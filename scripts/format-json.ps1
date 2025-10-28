param(
  [string]$Path,
  [int]$Depth = 100
)

$ErrorActionPreference = 'Stop'

function Read-Content {
  param([string]$p)
  if ($p) { return Get-Content -Path $p -Raw -ErrorAction Stop }
  if ($Host.Name -like '*Visual Studio*') {
    # VS Code integrated terminal supports pipeline input; still try stdin
    return [Console]::In.ReadToEnd()
  }
  return [Console]::In.ReadToEnd()
}

$raw = Read-Content -p $Path
if ([string]::IsNullOrWhiteSpace($raw)) { throw 'No input JSON provided (stdin or -Path).'}

try {
  $obj = $raw | ConvertFrom-Json -ErrorAction Stop
}
catch {
  Write-Error "Invalid JSON input: $($_.Exception.Message)"
  exit 1
}

$obj | ConvertTo-Json -Depth $Depth
