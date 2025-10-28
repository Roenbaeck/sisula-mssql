## Quick orientation (read first)

- Purpose: a minimal Sisula-like templating engine that runs inside Microsoft SQL Server via SQLCLR.
- Quick path to play: build `bin/SisulaRenderer.dll`, enable CLR in a dev DB, register assemblies, create the T-SQL wrapper, then run the sample in `sql/test_render.sql`.

## Key files and where to look

- `clr/SisulaRenderer.cs` — the entire renderer implementation (token syntax, foreach handling, JSON binding access). Use this to implement new template features.
- `scripts/build.ps1` — how the DLL is compiled. It uses csc and requires `lib/Newtonsoft.Json.dll` to be present.
- `sql/EnableClr.sql`, `sql/assemblies/CreateAssemblies.sql`, `sql/CreateFunction.sql` — DB steps to enable CLR and register the function `dbo.fn_sisulate`.
- `templates/CreateTypedTables.sqlslt` — example template demonstrating foreach blocks, token usage, and common patterns to follow.

## Project-specific conventions

- Template blocks are delimited by /*~ and ~*/. Everything outside blocks is treated as literal SQL and passed through unchanged.
- In-block language supports `foreach <var> in <path> ... end` (single-level loops). The loop body is rendered per item with the loop variable injected into the JSON context.
- Token expansion forms supported: `$path.to.value$` and `${path.to.value}$`. Paths support bracket indexing like `source.parts[0].name`.
- Bindings are passed as a single JSON document to the CLR function. Use existing templates for examples of JSON shapes (see `templates/` and `sql/test_render.sql`).

## Typical developer workflows (explicit commands)

- Build the CLR DLL (PowerShell, from `scripts`):

  Open PowerShell in `scripts` and run: `./build.ps1`

  Ensure `lib/Newtonsoft.Json.dll` exists first. The script compiles `clr/SisulaRenderer.cs` with csc into `bin/SisulaRenderer.dll`.

- Enable CLR and register assemblies in the target database (dev):

  1. Run `sql/EnableClr.sql` to enable CLR (dev only).
  2. Run `sql/assemblies/CreateAssemblies.sql` to create the Newtonsoft and SisulaRenderer assemblies.
  3. Run `sql/CreateFunction.sql` to create `dbo.fn_sisulate`.

- Run the sample render: open `sql/test_render.sql` (contains a sample template and bindings) and execute its contents to view `rendered` output.

## Common change patterns and examples

- To add a new template feature (for example, conditionals or nested foreach):
  - Update `clr/SisulaRenderer.cs`. The renderer is small and single-file: add parsing in `RenderBlock` and inline expansion in `RenderInline`.
  - Keep the public T-SQL signature unchanged (`fn_sisulate(nvarchar(max), nvarchar(max))`).
  - Add a focused unit-style SQL example in `sql/test_render.sql` demonstrating the new syntax.

- To change compilation or dependencies:
  - Update `scripts/build.ps1` and add required DLLs under `lib/`. The build process uses csc directly so prefer .NET Framework-compatible binaries.

## Integration points and constraints

- The CLR depends on `Newtonsoft.Json.dll` and the Microsoft SQL CLR host. For production you must sign assemblies and enable `clr strict security` appropriately — this repo keeps the MWE simple.
- The renderer returns rendered SQL as an NVARCHAR(max) from `dbo.fn_sisulate`. Consumers are expected to execute that SQL in the DB if desired.

## Examples from the codebase (copy/paste friendly)

- Foreach example (from `templates/CreateTypedTables.sqlslt`):

  /*~
  foreach part in source.parts
  CREATE TABLE [$S_SCHEMA].[$part.qualified$_Typed] (...);
  end
  ~*/

- Token example: `-- Generated: $VARIABLES.GENERATED_AT$` expands the JSON path `VARIABLES.GENERATED_AT`.

## When to ask for human review

- Changes that alter rendered SQL semantics (DDL/DML generation), security model (assembly permissions), or the public function contract require a code review and a DB deployment plan.

## Where to add tests and docs

- Add small SQL render samples to `sql/test_render.sql` as regression checks. Prefer tiny, self-contained examples that show input JSON and expected rendered text.

---

