-- 1) Load template from file (or paste it here)
DECLARE @tpl nvarchar(max) = (
  SELECT * FROM OPENROWSET(BULK N'c:\Users\LRO2\sisula\mssql-sisula\templates\CreateTypedTables.sqlslt', SINGLE_CLOB) AS T
);

-- 2) Prepare bindings JSON
DECLARE @bindings nvarchar(max) = N'{
  "S_SCHEMA": "dbo",
  "TIMESTAMP": "SYSUTCDATETIME()",
  "VARIABLES": {
    "USERNAME": "' + SUSER_SNAME() + '",
    "COMPUTERNAME": "DEVBOX",
    "USERDOMAIN": "LOCAL",
    "GENERATED_AT": "' + CONVERT(nvarchar(33), SYSUTCDATETIME(), 126) + '"
  },
  "source": {
    "qualified": "DemoSource",
    "name": "Demo Source",
    "parts": [
      { "qualified": "PartA", "name": "PartA" },
      { "qualified": "PartB", "name": "PartB" }
    ]
  }
}';

-- 3) Render
SELECT dbo.fn_sisulate(@tpl, @bindings) AS rendered;

-- Optional: execute
-- DECLARE @sql nvarchar(max) = dbo.fn_sisulate(@tpl, @bindings);
-- EXEC(@sql);
