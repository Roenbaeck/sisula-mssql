# mssql-sisula (MWE)

A minimal Sisula-like templating engine running inside SQL Server using SQLCLR.

Features
- SQL as outer language; templated blocks delimited by `/*~ ... ~*/`.
- In-block: `foreach` loops and `$path.to.value$` token expansion.
- Bindings passed as a single JSON document (e.g., `S_SCHEMA`, `VARIABLES`, `source.parts`).

Quick start
1. Build DLL
   - Copy Newtonsoft.Json.dll (13.x) into `lib/`.
   - Open PowerShell in `mssql-sisula/scripts` and run:
     - `./build.ps1`

2. Enable CLR (DEV ONLY)
   - Run `sql/EnableClr.sql` in your target database.

3. Register assemblies and function
   - Run `sql/assemblies/CreateAssemblies.sql`.
   - Run `sql/CreateFunction.sql`.

4. Render the sample
   - Run `sql/test_render.sql`.
   - Inspect `rendered` output; optionally execute it.

Notes
- For production, prefer signed assemblies and leave `clr strict security` enabled.
- Next steps: add session-based binding catalog (Begin/Bind/Render), conditionals, and richer syntax parity with original Sisula.

## AI assistant onboarding
If you're using an AI coding assistant, start here:
- See `.github/copilot-instructions.md` for a compact project brief and useful prompts.
