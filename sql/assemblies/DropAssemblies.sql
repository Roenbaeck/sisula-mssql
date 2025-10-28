IF OBJECT_ID('dbo.fn_sisulate') IS NOT NULL DROP FUNCTION dbo.fn_sisulate;
IF EXISTS (SELECT 1 FROM sys.assemblies WHERE name = N'SisulaRenderer') DROP ASSEMBLY [SisulaRenderer];
IF EXISTS (SELECT 1 FROM sys.assemblies WHERE name = N'NewtonsoftJson') DROP ASSEMBLY [NewtonsoftJson];
GO