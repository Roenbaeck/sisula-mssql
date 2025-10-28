-- ===== Test template =====
-- Header
/*~
-- Generated: $VARIABLES.GENERATED_AT$
-- By: $VARIABLES.USERNAME$ on $VARIABLES.COMPUTERNAME$ ($VARIABLES.USERDOMAIN$)
CREATE PROCEDURE [$SCHEMA$].[$SOURCE$_CreateTypedTables] AS
BEGIN
    SET NOCOUNT ON;

	$/foreach t in tables
	-- Create: $t.table$_Staging
	CREATE TABLE [$SCHEMA$].[$t.table$_Staging] (
		$/foreach c in t.columns
		[$c.name$] $c.type$,
		$/end 
		[created_at] datetime2 not null default ''$TIMESTAMP$''
	);
	$/end
~*/
	-- The End
END
GO
