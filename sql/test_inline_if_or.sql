-- Quick test for inline IF OR operator
DECLARE @template nvarchar(max) = N'
$/ foreach c in columns
-- Found ordinal $c.ordinal$ $/ if c.type = "untyped" or c.type == "varchar(1000) null" with expected type $/ endif
$/ endfor
';

DECLARE @bindings nvarchar(max) = N'{ "columns":[ { "ordinal": 10, "name": "b", "type": "untyped" } ] }';

DECLARE @rendered nvarchar(max) = dbo.fn_sisulate(@template, @bindings);
SELECT @rendered AS rendered_output;
