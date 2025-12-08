-- Test nested inline IF with logical OR and = alias
DECLARE @template nvarchar(max) = N'
$/ foreach c in columns
$/ if c.ordinal == 10
-- Found the 10th ordinal $/ if c.type = "untyped" or c.type == "varchar(1000) null" with expected type $/ endif
$/ endif
$/ endfor
';

DECLARE @bindings nvarchar(max) = N'
{ "columns": [
  { "ordinal": 9, "name": "a", "type": "int" },
  { "ordinal": 10, "name": "b", "type": "untyped" },
  { "ordinal": 11, "name": "c", "type": "varchar(1000) null" }
] }';

DECLARE @rendered nvarchar(max) = dbo.fn_sisulate(@template, @bindings);
SELECT @rendered AS rendered_output;
