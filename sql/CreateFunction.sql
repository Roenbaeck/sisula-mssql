IF OBJECT_ID('dbo.fn_sisulate') IS NOT NULL DROP FUNCTION dbo.fn_sisulate;
GO
CREATE FUNCTION dbo.fn_sisulate(@template nvarchar(max), @bindings nvarchar(max))
RETURNS nvarchar(max)
AS EXTERNAL NAME [SisulaRenderer].[SisulaRenderer].[fn_sisulate];
GO