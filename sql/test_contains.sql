-- Test contains() function
DECLARE @template nvarchar(max) = N'
$/ foreach c in columns
$/ if contains(c.type, "char")
-- Column $c.name$ is character-based (type: $c.type$)
$/ endif
$/ endfor
';

DECLARE @bindings nvarchar(max) = N'
{
  "columns": [
    {"name": "id", "type": "int not null"},
    {"name": "name", "type": "varchar(100) null"},
    {"name": "code", "type": "char(20) null"},
    {"name": "amount", "type": "decimal(10,2) null"},
    {"name": "description", "type": "nvarchar(max) null"}
  ]
}';

DECLARE @rendered nvarchar(max) = dbo.fn_sisulate(@template, @bindings);

SELECT @rendered AS rendered_output;
