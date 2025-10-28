# mssql-sisula (MWE)

A minimal Sisula-like templating engine running inside SQL Server using SQLCLR.

Features
- SQL as outer language; templated blocks delimited by `/*~ ... ~*/`.
- Line directives inside blocks using `$/ foreach ...` and `$/ end` (nesting supported).
- Token expansion with `$path.to.value$` and `${path.to.value}$`.
- Bindings passed as a single JSON document (e.g., `S_SCHEMA`, `VARIABLES`, `source.parts`).

Quick start
1. Build DLL
   - Open PowerShell in `mssql-sisula/scripts` and run:
     - `./build.ps1`

2. DBA prerequisites (instance-level)
   - Ensure CLR is enabled on the instance: `EXEC sp_configure 'clr enabled', 1; RECONFIGURE;`
   - Keep `clr strict security` enabled (recommended). Use a trusted/signing approach instead of disabling it.

3. Install into your database (recommended)
   - PowerShell (Windows auth): `pwsh -File scripts/install.ps1 -Server "YOURSERVER" -Database "YourTestDb"`
   - Add `-RegisterTrustedAssembly` to register the SHA-512 in master when strict security is ON.
   - Optional: install templates into the DB for easy reuse: add `-InstallTemplates`. Templates are read from `templates/*.sql`.

4. Render the sample
   - Run `sql/test_render.sql`.
   - Inspect `rendered` output; optionally execute it.

Templates
- Author templates as `.sql` files under `templates/` to get proper SQL syntax highlighting in SSMS/VS Code.
- Use `/*~ ... ~*/` blocks with `$/ foreach ...` directives and `$...$` tokens.
- Install them into the database with `scripts/install.ps1 -InstallTemplates`, then call `dbo.fn_sisulate_named('<name>', @bindings)`.

Notes
- Uses SQL Server JSON functions (JSON_VALUE/JSON_QUERY/OPENJSON); no third-party JSON libraries required. Scalar tokens are limited to NVARCHAR(4000).
- For production, leave `clr strict security` enabled and trust the assembly via `sys.sp_add_trusted_assembly` using the assembly's SHA-512 hash.
- Next steps: add session-based binding catalog (Begin/Bind/Render), conditionals, and richer syntax parity with original Sisula.

### Trusting the assembly (strict security ON)
You can trust a specific build by registering its SHA-512 hash at the server level (sysadmin required). Example flow:

1. Compute the hash on the deployment machine:
   - PowerShell: `(Get-FileHash bin\SisulaRenderer.dll -Algorithm SHA512).Hash`
2. On the SQL Server instance (master):
   - `EXEC sys.sp_add_trusted_assembly @hash = 0x<hex-64-bytes>, @description = N'SisulaRenderer';`
3. Deploy the assembly in the target database with `PERMISSION_SET = SAFE` (the installer does this).

## AI assistant onboarding
If you're using an AI coding assistant, start here:
- See `.github/copilot-instructions.md` for a compact project brief and useful prompts.
