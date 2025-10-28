param(
  [Parameter(Mandatory=$true)] [string]$Server,
  [Parameter(Mandatory=$true)] [string]$Database,
  [switch]$RegisterTrustedAssembly = $false,
  [string]$TrustedAssemblyDescription = 'SisulaRenderer',
  [switch]$PruneTrustedAssemblies = $false,
  [switch]$TrustServerCertificate = $true,
  [switch]$IntegratedSecurity = $true,
  [string]$User,
  [string]$Password,
  [ValidateSet('SAFE','EXTERNAL_ACCESS','UNSAFE')] [string]$PermissionSet = 'SAFE',
  [switch]$Recreate = $true,
  [switch]$InstallTemplates = $false,
  [string]$TemplatesPath
)

$ErrorActionPreference = 'Stop'

function New-ConnectionString([string]$server, [string]$db) {
  if ($IntegratedSecurity) {
    return "Server=$server;Database=$db;Integrated Security=True;TrustServerCertificate=$TrustServerCertificate"
  } else {
    if (-not $User -or -not $Password) { throw "Provide -User and -Password or use -IntegratedSecurity" }
    return "Server=$server;Database=$db;User ID=$User;Password=$Password;TrustServerCertificate=$TrustServerCertificate"
  }
}

function Invoke-Sql([string]$connStr, [string]$sql) {
  $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
  $cmd = $conn.CreateCommand()
  $cmd.CommandTimeout = 120
  try {
    $conn.Open()
    $cmd.CommandText = $sql
    [void]$cmd.ExecuteNonQuery()
  }
  catch {
    Write-Host "--- SQL Error (ExecuteNonQuery) ---" -ForegroundColor Red
    Write-Host ($_.Exception.Message) -ForegroundColor Red
    Write-Host "SQL (truncated):" -ForegroundColor Yellow
    $preview = if ($sql.Length -gt 800) { $sql.Substring(0,800) + "..." } else { $sql }
    Write-Host $preview
    throw
  }
  finally {
    if ($conn.State -ne 'Closed') { $conn.Close() }
    $conn.Dispose()
  }
}

function Invoke-SqlParams([string]$connStr, [string]$sql, [hashtable]$parameters) {
  $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
  $cmd = $conn.CreateCommand()
  $cmd.CommandTimeout = 120
  try {
    $conn.Open()
    $cmd.CommandText = $sql
    foreach ($k in $parameters.Keys) {
      $v = $parameters[$k]
      $p = $cmd.Parameters.Add("@$k", [System.Data.SqlDbType]::Variant)
      if ($v -is [byte[]]) {
        $p.SqlDbType = [System.Data.SqlDbType]::VarBinary
        # Hash is 64 bytes; assembly bytes can be large
        if ($k -eq 'hash') {
          $p.Size = 64
        }
        else {
          $p.Size = if ($v.Length -gt 8000) { -1 } else { $v.Length }
        }
      } elseif ($v -is [string]) {
        $p.SqlDbType = [System.Data.SqlDbType]::NVarChar
        # Use NVARCHAR(MAX) when needed; otherwise NVARCHAR(4000)
        $p.Size = if ($v.Length -gt 4000) { -1 } else { 4000 }
      }
      $p.Value = $v
    }
    [void]$cmd.ExecuteNonQuery()
  }
  catch {
    Write-Host "--- SQL Error (ExecuteNonQuery with parameters) ---" -ForegroundColor Red
    Write-Host ($_.Exception.Message) -ForegroundColor Red
    Write-Host "SQL (truncated):" -ForegroundColor Yellow
    $preview = if ($sql.Length -gt 800) { $sql.Substring(0,800) + "..." } else { $sql }
    Write-Host $preview
    Write-Host "Parameters:" -ForegroundColor Yellow
    foreach ($k in $parameters.Keys) {
      $v = $parameters[$k]
      $type = ($v.GetType().FullName)
      $len = if ($v -is [byte[]]) { $v.Length } elseif ($v -is [string]) { $v.Length } else { '' }
      Write-Host ("  @{0} : {1} {2}" -f $k, $type, $len)
    }
    throw
  }
  finally {
    if ($conn.State -ne 'Closed') { $conn.Close() }
    $conn.Dispose()
  }
}

# Resolve repo/bin and DLL
$ScriptRoot = $PSScriptRoot; if (-not $ScriptRoot) { $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path }
$RepoRoot = (Resolve-Path (Join-Path $ScriptRoot "..\")).Path
$DllPath = Join-Path $RepoRoot "bin\SisulaRenderer.dll"
if (!(Test-Path $DllPath)) { throw "Assembly not found: $DllPath. Build first (scripts/build.ps1)." }

# Read DLL and produce hex literal 0x....
$bytes = [System.IO.File]::ReadAllBytes($DllPath)
$hex = '0x' + ([System.BitConverter]::ToString($bytes).Replace('-', ''))

# Connections
$csMaster = New-ConnectionString -server $Server -db 'master'
$csDb = New-ConnectionString -server $Server -db $Database

Write-Host "Installing SisulaRenderer to $Server/$Database with $PermissionSet (strict security expected ON)"

if ($RegisterTrustedAssembly) {
  Write-Host "Registering trusted assembly (SHA-512) in master..."
  $hashBytes = (Get-FileHash -Algorithm SHA512 -Path $DllPath).Hash
  # Convert hex string to byte[]
  $hashByteArray = for ($i = 0; $i -lt $hashBytes.Length; $i += 2) { [Convert]::ToByte($hashBytes.Substring($i, 2), 16) }
  $trustSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.trusted_assemblies WHERE [hash] = @hash)
  EXEC sys.sp_add_trusted_assembly @hash = @hash, @description = @desc;
"@
  Invoke-SqlParams -connStr $csMaster -sql $trustSql -parameters @{ hash = ([byte[]]$hashByteArray); desc = [string]$TrustedAssemblyDescription }

  if ($PruneTrustedAssemblies) {
    Write-Host "Pruning older trusted assemblies with description '$TrustedAssemblyDescription'..."
    $pruneSql = @"
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
  SELECT [hash] FROM sys.trusted_assemblies WHERE [description] = @desc AND [hash] <> @hash;
DECLARE @h VARBINARY(64);
OPEN cur;
FETCH NEXT FROM cur INTO @h;
WHILE @@FETCH_STATUS = 0
BEGIN
  BEGIN TRY
    EXEC sys.sp_drop_trusted_assembly @hash = @h;
  END TRY BEGIN CATCH
    -- ignore failures; continue with others
  END CATCH
  FETCH NEXT FROM cur INTO @h;
END
CLOSE cur; DEALLOCATE cur;
"@;
    Invoke-SqlParams -connStr $csMaster -sql $pruneSql -parameters @{ hash = ([byte[]]$hashByteArray); desc = [string]$TrustedAssemblyDescription }
  }
}

Write-Host "Dropping existing function and assembly (if present) in $Database..."
# Deploy into DB: drop old, create assembly from hex, create function
# 1) Drop function (dynamic SQL inside IF to avoid parse-time errors)
$dropFnSql = @'
IF OBJECT_ID(N'dbo.fn_sisulate') IS NOT NULL
BEGIN
  DECLARE @sql nvarchar(4000) = N'DROP FUNCTION dbo.fn_sisulate;';
  EXEC(@sql);
END
'@
Write-Host "Dropping existing function (if present) in $Database..."
Invoke-Sql -connStr $csDb -sql $dropFnSql

# 2) Drop assembly (only if present)
$dropAsmSql = @'
IF EXISTS (SELECT 1 FROM sys.assemblies WHERE name = N'SisulaRenderer')
BEGIN
  DROP ASSEMBLY [SisulaRenderer];
END
'@
Write-Host "Dropping existing assembly (if present) in $Database..."
Invoke-Sql -connStr $csDb -sql $dropAsmSql

# CREATE ASSEMBLY from parameterized bytes to avoid huge literal parsing issues
$createAsm = @"
CREATE ASSEMBLY [SisulaRenderer]
FROM @bytes
WITH PERMISSION_SET = $PermissionSet;
"@
Write-Host "Creating assembly in $Database (PERMISSION_SET = $PermissionSet)..."
Write-Host "Creating assembly in $Database (PERMISSION_SET = $PermissionSet)..."
Invoke-SqlParams -connStr $csDb -sql $createAsm -parameters @{ bytes = ([byte[]]$bytes) }

$createFn = @'
CREATE FUNCTION dbo.fn_sisulate(@template nvarchar(max), @bindings nvarchar(max))
RETURNS nvarchar(max)
AS EXTERNAL NAME [SisulaRenderer].[SisulaRenderer].[fn_sisulate];
'@
Write-Host "Creating function dbo.fn_sisulate in $Database..."
Invoke-Sql -connStr $csDb -sql $createFn

# Optional: install templates into the database and create a named-template wrapper
if (-not $TemplatesPath -or [string]::IsNullOrWhiteSpace($TemplatesPath)) {
  $TemplatesPath = Join-Path $RepoRoot 'templates'
}

if ($InstallTemplates) {
  Write-Host "Ensuring dbo.SisulaTemplates table exists in $Database..."
  $ensureTable = @'
IF OBJECT_ID(N'dbo.SisulaTemplates') IS NULL
BEGIN
  CREATE TABLE dbo.SisulaTemplates(
    name sysname NOT NULL PRIMARY KEY,
    content nvarchar(max) NOT NULL,
    modified_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
END
'@
  Invoke-Sql -connStr $csDb -sql $ensureTable

  if (Test-Path $TemplatesPath) {
    $files = Get-ChildItem -Path $TemplatesPath -Filter '*.sql' -File -ErrorAction SilentlyContinue
    foreach ($f in $files) {
      $name = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
      $content = Get-Content -Path $f.FullName -Raw
      Write-Host ("Installing template '{0}' from {1}" -f $name, $f.Name)
      $upsert = @"
IF EXISTS (SELECT 1 FROM dbo.SisulaTemplates WHERE name = @name)
  UPDATE dbo.SisulaTemplates SET content = @content, modified_at = SYSUTCDATETIME() WHERE name = @name;
ELSE
  INSERT INTO dbo.SisulaTemplates(name, content) VALUES(@name, @content);
"@
      Invoke-SqlParams -connStr $csDb -sql $upsert -parameters @{ name = [string]$name; content = [string]$content }
    }
  } else {
    Write-Host "Templates path not found: $TemplatesPath" -ForegroundColor Yellow
  }

}

Write-Host "Install completed. You can test with: sql/test_render.sql"