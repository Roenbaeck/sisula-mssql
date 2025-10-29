-- ===== Test template =====
-- Header
-- Generated: ${VARIABLES.GENERATED_AT}$
-- By: ${VARIABLES.USERNAME}$ on ${VARIABLES.COMPUTERNAME}$ (${VARIABLES.USERDOMAIN}$)
CREATE PROCEDURE [${SCHEMA}$].[${SOURCE}$_CreateTypedTables] AS
BEGIN
    SET NOCOUNT ON;

	$/ foreach t in tables 
	-- Create: ${t.table}$_Staging
	CREATE TABLE [${SCHEMA}$].[${t.table}$_Staging] (
		$/ foreach c in t.columns order by c.ordinal 
		[${c.name}$] ${c.type}$, -- column number ${LOOP.index}$ (ordinal ${c.ordinal}$)
		$/ if LOOP.first 
		-- that was the first column
		$/ endif
		$/ endfor
		[created_at] datetime2 not null default ''${TIMESTAMP}$''
	);
	$/ endfor
	-- The End
END
GO
