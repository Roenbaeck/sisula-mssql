# mssql-sisula (MWE)

A minimal Sisula-like templating engine running inside SQL Server using SQLCLR.

For the full language reference and examples see `SISULA.md` in this repository. The language reference documents tokens, directives (foreach/if), loop metadata method calls, expression syntax, and JSON resolution behavior.

Quick start
1. Build DLL
   - Open PowerShell in `mssql-sisula/scripts` and run:
     - `./build.ps1`

2. DBA prerequisites and trust
   - Ensure CLR is enabled on the instance: `EXEC sp_configure 'clr enabled', 1; RECONFIGURE;`
   - Keep `clr strict security` enabled (recommended). Use a trusted/signing approach instead of disabling it.
   - To trust this build when `clr strict security` is ON, compute the SHA-512 and register it via `sys.sp_add_trusted_assembly` on the server (sysadmin required).

3. Install into your database (recommended)
   - PowerShell (Windows auth): `pwsh -File scripts/install.ps1 -Server "YOURSERVER" -Database "YourTestDb"`
   - Add `-RegisterTrustedAssembly` to register the SHA-512 in master when strict security is ON.
   - Optional: install templates into the DB for easy reuse: add `-InstallTemplates`. Templates are read from `templates/*.sql`.

4. Render the sample
   - Run `sql/test_render.sql` and inspect the `rendered` output.

Notes
- Uses SQL Server JSON functions (JSON_VALUE/JSON_QUERY/OPENJSON); no third-party JSON libraries required. Scalar tokens are limited to NVARCHAR(4000).

## AI assistant onboarding
If you're using an AI coding assistant, start here:
- See `.github/copilot-instructions.md` for a compact project brief and useful prompts.

