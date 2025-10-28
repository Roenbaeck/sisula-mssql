/*~
$/ foreach part in source.parts
-- Dropping table to enforce Deferred Name Resolution: $part.qualified$_Typed
IF Object_ID('$S_SCHEMA$.$part.qualified$_Typed', 'U') IS NOT NULL
DROP TABLE [$S_SCHEMA$].[$part.qualified$_Typed];
$/ end
~*/

/*~
-- Header
-- Generated: $VARIABLES.GENERATED_AT$
-- By: $VARIABLES.USERNAME$ on $VARIABLES.COMPUTERNAME$ ($VARIABLES.USERDOMAIN$)
CREATE PROCEDURE [$S_SCHEMA$].[$source.qualified$_CreateTypedTables] (
    @agentJobId uniqueidentifier = null,
    @agentStepId smallint = null
)
AS
BEGIN
SET NOCOUNT ON;
~*/

/*~
$/ foreach part in source.parts
-- Create: $part.qualified$_Typed
CREATE TABLE [$S_SCHEMA$].[$part.qualified$_Typed] (
    /*~ $/ foreach col in part.columns ~*/
    [$col.name$] $col.type$,
    /*~ $/ end ~*/
    _timestamp datetime2 not null default $TIMESTAMP$
);
$/ end
~*/

/*~
-- Footer
END
GO
~*/
