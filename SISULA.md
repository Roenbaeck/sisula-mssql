# Sisula language reference

This document describes the small Sisula-like template language implemented by the `SisulaRenderer` SQLCLR function.

Blocks and tokens
- Template blocks are delimited by `/*~ ... ~*/`. Everything outside blocks is passed through unchanged. If a template has no `/*~ ... ~*/` delimiters, the entire template is treated as a Sisula script (tokens + line directives).
- Tokens are written as `$path.to.value$` or `${path.to.value}$` and support bracket indexing (e.g. `source.parts[0].name`). Token values are resolved against the JSON bindings or loop variables.
- Path segments may use Unicode letters (e.g. `$VARIABLES.ÅÄÖ$`); they are quoted appropriately in JSON queries.

Line directives
- All line directives require the `$/` prefix.
- Foreach:
  - Syntax: `$/ foreach <var> in <path> [where <expr>] [order by <path> [desc]]` ... `$/ endfor`
  - Iterates over a JSON array found at `<path>` (supports loop variable scoping and nesting).
  - Optional `where` filters items using the same expression language as `$/ if`.
  - Optional `order by` supports numeric-aware sorting and an optional `desc` flag.
- If:
  - Block form: `$/ if <condition>` ... `$/ endif` — conditional rendering of blocks.
  - Single-line form (inline-if): `$/ if <cond> <content> $/ endif` — renders `<content>` inline when `<cond>` is true. The inline content respects the indentation where the directive appears.
    - Inline-if directives can also appear inside a content line to add or remove inline fragments (useful for trailing commas or comments that depend on metadata).

Comments
- Line comments: start a line with `$-` (optionally indented) to remove it from the rendered output.
- Inline comments: wrap comment text as `$- ... -$` to drop the span while keeping the surrounding content.
- Comments are stripped before token or directive evaluation.

Loop metadata
- Each `foreach` injects per-loop metadata that's accessible by the loop variable name via method calls.
    - Use the method form to access loop metadata: `varName.index()`, `varName.count()`, `varName.first()`, `varName.last()`.
    - Tokens can reference these methods directly, e.g. `$c.index()$`, `$t.count()$`.
  - Only the method form is supported to avoid ambiguity in nested loops and path parsing.

Expression language
- Comparison operators: `==, !=, >=, <=, >, <`.
- Functions: `contains(x,"y")`, `startswith(x,"y")`, `endswith(x,"y")`.
- String literals use double quotes (`"value"`). Escape a double quote inside a literal with `""`.
- Single-quoted literals are not supported (use double quotes exclusively).
- Truthy checks on paths: null/empty/false/"0"/"null" are falsey.
- Expressions are used by `$/ if` and `foreach where`.

JSON binding and resolution
- Bindings are passed as a single JSON document to `fn_sisulate(template, bindingsJson)`.
- Resolution uses SQL Server JSON functions: `JSON_VALUE`, `JSON_QUERY`, and `OPENJSON`. No third-party JSON libraries are used.
- `foreach` uses `OPENJSON` to enumerate arrays; `JsonRead` uses `JSON_VALUE` then `JSON_QUERY` for scalar/complex reads.
- Scalar tokens are limited to NVARCHAR(4000) when read via `JSON_VALUE`.

Authoring and installing templates
- Author templates as `.sql` files under `templates/` to get proper SQL syntax highlighting in SSMS/VS Code.
- Install templates into the DB with `scripts/install.ps1 -InstallTemplates`.

Examples

Inline token example:

    SELECT $S_SCHEMA$.$table.name$

Foreach example with order by:

    /*~
    $/ foreach part in source.parts order by part.ordinal
    CREATE TABLE [$S_SCHEMA$].[$part.name$] (...);
    $/ endfor
    ~*/

Foreach example with where:

    /*~
    $/ foreach part in source.parts where part.type == "table"
    DROP TABLE [$S_SCHEMA$].[$part.name$];
    $/ endfor
    ~*/

Nested foreach example with loop metadata:

    /*~
    $/ foreach table in source.tables
    $- loop over tables
    $/ if t.first()
    -- First table comment
    $/ endif
    $/ foreach col in table.columns
    $- loop over columns
    $/ if c.last()
    ALTER TABLE [$S_SCHEMA$].[$table.name$] ADD [$col.name$] $col.type$;
    $/ endif
    $/ endfor
    $/ endfor
    ~*/

Inline-if example (single-line, follows indentation):

        $/ if c.first() -- first column $/ endif

Inline-if embedded within a line (e.g., mark the last column):

    [$c.name$] $c.type$$/ if c.last() -- last column marker $/ endif $- inline sisula comment -$

Multi-line if example with truthy check:

    $/ if source.enabled
    -- Enable feature
    $/ endif

Multi-line if example with function:

    $/ if contains(table.name, "temp")
    -- Temporary table logic
    $/ endif

Multi-line if example with comparison:

    $/ if table.priority > 5
    -- High priority table
    $/ endif

