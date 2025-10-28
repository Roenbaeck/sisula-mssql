## Quick orientation (read first)

- Purpose: a minimal Sisula-like templating engine that runs inside Microsoft SQL Server via SQLCLR.
- Quick path to play: build `bin/SisulaRenderer.dll`, register assemblies and function, then run the sample in `sql/test_render.sql`. Enabling CLR and trusted assembly registration are DBA tasks.

## Key files and where to look

- `clr/SisulaRenderer.cs` — the entire renderer implementation (token syntax, line directives, JSON binding access). Use this to implement new template features.
- `scripts/build.ps1` — how the DLL is compiled (csc). No third‑party deps required.
- `sql/assemblies/CreateAssemblies.sql`, `sql/CreateFunction.sql` — DB steps to register the function `dbo.fn_sisulate`. Enabling CLR and trusted assembly registration are DBA tasks (instance-level).
- `templates/CreateTypedTables.sql` — example template demonstrating line directives (`$/ foreach`, `$/ end`), token usage, and common patterns to follow.

## Project-specific conventions

- Template blocks are delimited by /*~ and ~*/. Everything outside blocks is treated as literal SQL and passed through unchanged. If a template has no /*~ ~*/ delimiters at all, the entire template is treated as Sisula code (tokens + $/ directives).
- In-block language supports line directives: `$/ foreach <var> in <path>` ... `$/ end` (nesting supported). The loop body is rendered per item with the loop variable injected into the JSON context.
- Token expansion forms supported: `$path.to.value$` and `${path.to.value}$`. Paths support bracket indexing like `source.parts[0].name`.
- Bindings are passed as a single JSON document to the CLR function. Resolution uses SQL Server JSON functions (JSON_VALUE/JSON_QUERY/OPENJSON). Use existing templates for examples of JSON shapes (see `templates/` and `sql/test_render.sql`).

## Typical developer workflows (explicit commands)

- Build the CLR DLL (PowerShell, from `scripts`):

  Open PowerShell in `scripts` and run: `./build.ps1`

  The script compiles `clr/SisulaRenderer.cs` with csc into `bin/SisulaRenderer.dll`.

- Register assemblies and function (DB):

  1. Ensure instance-level prerequisites are met by a DBA (CLR enabled, `clr strict security` left ON; assembly trusted via SHA-512).
  2. Run `sql/assemblies/CreateAssemblies.sql` to create the SisulaRenderer assembly.
  3. Run `sql/CreateFunction.sql` to create `dbo.fn_sisulate`.

- Run the sample render: open `sql/test_render.sql` (contains a sample template and bindings) and execute its contents to view `rendered` output.

## Common change patterns and examples

- To add a new template feature (for example, conditionals or nested foreach):
  - Update `clr/SisulaRenderer.cs`. The renderer is small and single-file: add parsing in `RenderBlock` and inline expansion in `RenderInline`.
  - Keep the public T-SQL signature unchanged (`fn_sisulate(nvarchar(max), nvarchar(max))`).
  - Add a focused unit-style SQL example in `sql/test_render.sql` demonstrating the new syntax.

- To change compilation:
  - Update `scripts/build.ps1`. The build uses csc targeting .NET Framework and only first‑party assemblies.

## Integration points and constraints

- The CLR depends only on the Microsoft SQL CLR host and SQL Server JSON features (2016+). For production keep `clr strict security` ON and trust the assembly via `sys.sp_add_trusted_assembly` (SHA-512).
- The renderer returns rendered SQL as an NVARCHAR(max) from `dbo.fn_sisulate`. Consumers are expected to execute that SQL in the DB if desired.

## Examples from the codebase (copy/paste friendly)

- Foreach example (from `templates/CreateTypedTables.sql`):

  /*~
  $/ foreach part in source.parts
  CREATE TABLE [$S_SCHEMA$].[$part.qualified$_Typed] (...);
  $/ end
  ~*/

- Token rules: Always delimit tokens with both a starting and ending dollar sign. Examples: `$S_SCHEMA$`, `$source.parts[0].name$`, `${VARIABLES.GENERATED_AT}$`. The bare form without a closing `$` (e.g., `$S_SCHEMA`) is not supported.

## When to ask for human review

- Changes that alter rendered SQL semantics (DDL/DML generation), security model (assembly permissions), or the public function contract require a code review and a DB deployment plan.

## Where to add tests and docs

- Add small SQL render samples to `sql/test_render.sql` as regression checks. Prefer tiny, self-contained examples that show input JSON and expected rendered text.

---

