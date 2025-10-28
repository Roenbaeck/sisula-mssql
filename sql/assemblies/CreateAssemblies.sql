-- Adjust paths if needed
DECLARE @Newtonsoft NVARCHAR(4000) = N'c:\Users\LRO2\sisula\mssql-sisula\lib\Newtonsoft.Json.dll';
DECLARE @Renderer  NVARCHAR(4000) = N'c:\Users\LRO2\sisula\mssql-sisula\bin\SisulaRenderer.dll';

-- Drop if exist to allow redeploy
IF EXISTS (SELECT 1 FROM sys.assemblies WHERE name = N'SisulaRenderer') DROP ASSEMBLY [SisulaRenderer];
IF EXISTS (SELECT 1 FROM sys.assemblies WHERE name = N'NewtonsoftJson') DROP ASSEMBLY [NewtonsoftJson];
GO

CREATE ASSEMBLY [NewtonsoftJson]
FROM N'c:\Users\LRO2\sisula\mssql-sisula\lib\Newtonsoft.Json.dll'
WITH PERMISSION_SET = SAFE;
GO

CREATE ASSEMBLY [SisulaRenderer]
FROM N'c:\Users\LRO2\sisula\mssql-sisula\bin\SisulaRenderer.dll'
WITH PERMISSION_SET = SAFE;
GO