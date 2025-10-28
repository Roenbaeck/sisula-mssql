# mssql-sisula (MWE)

A minimal Sisula-like templating engine running inside SQL Server using SQLCLR.

Features
- SQL as outer language; templated blocks are delimited by `/*~ ... ~*/`.
   - If a template contains no `/*~ ... ~*/` delimiters, the entire file is treated as a Sisula script (tokens + line directives).
- Line directives inside blocks (use the `$/` prefix):
   - `$/ foreach <var> in <path> [where <expr>] [order by <path> [desc]]` ... `$/ endfor`
      - Iterates over a JSON array at `<path>` (supports loop variable scoping).
      - Optional `where` expression filters items; uses the same expression language as `$/ if`.
      - Optional `order by` supports numeric-aware sorting and an optional `desc` flag.
   - `$/ if <condition>` ... `$/ endif` — conditional rendering of blocks.
   - Single-line inline-if is supported: `$/ if <cond> <content> $/ endif` (content respects the template indentation where the directive appears).
- Token expansion:
   - Inline tokens are written as `$path.to.value$` or `${path.to.value}$` and are resolved against the JSON bindings or loop variables.
   - Paths support bracket indexing: `source.parts[0].name`.
- Loop metadata (`$LOOP`): inside a `foreach` body a special `LOOP` variable is available with JSON fields:
   - `LOOP.index` (zero-based), `LOOP.count`, `LOOP.first` (boolean), `LOOP.last` (boolean).
   - `$LOOP` is injected as a JSON value and can be used in tokens and `$/ if` expressions.
- Expression language (used by `$/ if` and `foreach where`):
   - Comparison operators: `==, !=, >=, <=, >, <`.
   - Functions: `contains(x,'y')`, `startswith(x,'y')`, `endswith(x,'y')`.
   - Truthy checks on paths (null/empty/false/"0"/"null" are falsey).
- JSON binding and resolution:
   - Bindings are passed as a single JSON document to `fn_sisulate(template, bindingsJson)`.
   - Resolution uses SQL Server JSON functions: `JSON_VALUE`, `JSON_QUERY`, and `OPENJSON`. No third-party JSON libraries are used.
   - `foreach` uses `OPENJSON` to enumerate arrays; `JsonRead` uses `JSON_VALUE` then `JSON_QUERY` for scalar/complex reads.

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
- Use `/*~ ... ~*/` blocks with `$/` line directives and `$...$` tokens. Everything outside blocks is passed through unchanged.
- Example patterns:

   /*~
   $/ foreach part in source.parts order by part.ordinal
   CREATE TABLE [$S_SCHEMA$].[$part.name$] (...);
   $/ endfor
   ~*/

- Inline-if example (single-line, follows indentation):

      $/ if LOOP.first -- first column $/ endif

- Multi-line if example:

      $/ if S_SCHEMA == 'dbo'
      -- do dbo specific logic
      $/ endif

- Install templates into the DB with `scripts/install.ps1 -InstallTemplates` and call `dbo.fn_sisulate_named('<name>', @bindings)` to render stored templates.

Notes
- Uses SQL Server JSON functions (JSON_VALUE/JSON_QUERY/OPENJSON); no third-party JSON libraries required. Scalar tokens are limited to NVARCHAR(4000).
- For production, leave `clr strict security` enabled and trust the assembly via `sys.sp_add_trusted_assembly` using the assembly's SHA-512 hash.
- The project supports many of the common Sisula patterns (foreach, conditionals, inline tokens). If you extend syntax, add an example to `sql/test_render.sql` as a regression test.

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
