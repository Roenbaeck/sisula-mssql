/*
	Very simple test SP for fn_sisulate.

	2025-10-28	Lars Rönnbäck		CREATED
  2025-11-03  Lars Rönnbäck   Added loop metadata usage in template.
                              Extended the template example.

	EXEC Test_Sisulate
*/
CREATE OR ALTER PROC Test_Sisulate
AS
BEGIN

SET NOCOUNT ON;

DECLARE @template nvarchar(max) = N'
-- ===== Test template =====
-- Header
-- Generated: $VARIABLES.GENERATED_AT$
-- By: $VARIABLES.USERNAME$ on $VARIABLES.COMPUTERNAME$ ($VARIABLES.USERDOMAIN$)
$/ if VARIABLES.MYINTEGER == 1
-- My Integer is $VARIABLES.MYINTEGER$
$/ endif
$/ if VARIABLES.MYINTEGER == 2
-- This comment is not displayed
$/ endif
$/ if VARIABLES.MYLETTER == "A"
-- My Letter is $VARIABLES.MYLETTER$
$/ endif
$/ if VARIABLES.MYLETTER == "B"
-- This comment is not displayed
$/ endif
-- Svenska tecken: $VARIABLES.ÅÄÖ$
IF (1 > 1) PRINT ''Not likely''
CREATE PROCEDURE [$SCHEMA$].[$SOURCE$_CreateTypedTables] AS
BEGIN
    SET NOCOUNT ON;

	$/ foreach t in tables
	-- Create: $t.table$_Staging
	-- Number of tables: $t.count()$
	-- First column name is $t.columns[0].name$
	CREATE TABLE [$SCHEMA$].[$t.table$_Staging] (
		$- loop over some variables
		$-
		$/ foreach c in t.columns where c.ordinal > 1 order by c.ordinal 
		$/ if c.last() 
		-- here comes the last column
		$/ endif
		[$c.name$] $c.type$,$/ if c.ordinal -- $c.ordinal$ ($c.index()$) $/ endif		
		$/ if c.first() 
		-- that was the first column
		$/ endif
		$/ endfor
		$-
		-- $/ foreach c in t.columns C:$c.index()$ $/ endfor
		-- $/ foreach c in t.columns $/ if c.index() == 10 Index $c.index()$ found $/ endif $/ endfor
		[created_at] datetime2 not null default ''$TIMESTAMP$''
	);

	$/ endfor

	-- The End
END
GO
';

-- Build bindings JSON with proper quoting for dynamic values
DECLARE @username nvarchar(256) = SUSER_SNAME();
DECLARE @servername nvarchar(128) = @@SERVERNAME;
DECLARE @dbname nvarchar(128) = DB_NAME();
DECLARE @generated_at nvarchar(33) = CONVERT(nvarchar(33), SYSUTCDATETIME(), 126);
DECLARE @timestamp nvarchar(33) = @generated_at;

DECLARE @bindings nvarchar(max) = N'
{
  "SOURCE": "Lumera",
  "SCHEMA": "dbo",
  "VARIABLES": {
    "GENERATED_AT": "' + @generated_at + N'",
    "USERNAME": "' + STRING_ESCAPE(@username, 'json') + N'",
    "COMPUTERNAME": "' + STRING_ESCAPE(@servername, 'json') + N'",
    "USERDOMAIN": "' + STRING_ESCAPE(@dbname, 'json') + N'",
    "MYINTEGER": 1,
    "MYLETTER": "A",
    "ÅÄÖ": "åäö"
  },
  "TIMESTAMP": "' + @timestamp + N'",
  "tables": [
    {
      "schema": "dbo",
      "table": "_TMP_CONTRACT_STATUS",
      "columns": [
        {
          "ordinal": 1,
          "name": "INSURANCE_OFFICIAL_ID",
          "type": "char(20) null"
        },
        {
          "ordinal": 2,
          "name": "CONTRACT_ID",
          "type": "int null"
        },
        {
          "ordinal": 3,
          "name": "CONTRACT_STATUS",
          "type": "int null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "CLASS",
      "columns": [
        {
          "ordinal": 1,
          "name": "CLASS_NAME",
          "type": "char(128) not null"
        },
        {
          "ordinal": 2,
          "name": "CLASS_ID",
          "type": "int not null"
        },
        {
          "ordinal": 3,
          "name": "CLASS_GROUP",
          "type": "char(40) null"
        },
        {
          "ordinal": 4,
          "name": "ACTION_LOGGING",
          "type": "bit null"
        },
        {
          "ordinal": 6,
          "name": "APPLICATION_MANAGED_CLASS",
          "type": "bit null"
        },
        {
          "ordinal": 7,
          "name": "FOREIGN_KEY_SEARCH",
          "type": "bit null"
        },
        {
          "ordinal": 9,
          "name": "FOREIGN_KEY_ATTRIBUTES",
          "type": "char(100) null"
        },
        {
          "ordinal": 10,
          "name": "DEFAULT_SORT_ATTRIBUTES",
          "type": "varchar(1000) null"
        },
        {
          "ordinal": 11,
          "name": "DEFAULT_COUNT",
          "type": "int null"
        },
        {
          "ordinal": 12,
          "name": "DEFAULT_COUNT_WITH_PARENT",
          "type": "int null"
        },
        {
          "ordinal": 14,
          "name": "CLASS_CACHE_TIMESTAMP",
          "type": "datetime null"
        },
        {
          "ordinal": 15,
          "name": "CLASS_PACKAGE_NAME",
          "type": "char(40) null"
        },
        {
          "ordinal": 20,
          "name": "SUPER_CLASS",
          "type": "char(128) null"
        },
        {
          "ordinal": 21,
          "name": "CREATE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 22,
          "name": "CHANGE_ID",
          "type": "bigint null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "DOCUMENT_TYPE",
      "columns": [
        {
          "ordinal": 1,
          "name": "DOCUMENT_TYPE_ID",
          "type": "int not null"
        },
        {
          "ordinal": 2,
          "name": "DOCUMENT_TYPE_NAME",
          "type": "char(40) not null"
        },
        {
          "ordinal": 3,
          "name": "CREATED_DATE",
          "type": "datetime not null"
        },
        {
          "ordinal": 4,
          "name": "CREATED_BY",
          "type": "varchar(40) not null"
        },
        {
          "ordinal": 5,
          "name": "LATEST_CHANGED_DATE",
          "type": "datetime null"
        },
        {
          "ordinal": 6,
          "name": "LATEST_CHANGED_BY",
          "type": "varchar(40) null"
        },
        {
          "ordinal": 7,
          "name": "CREATE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 8,
          "name": "CHANGE_ID",
          "type": "bigint null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "EMPLOYMENT",
      "columns": [
        {
          "ordinal": 1,
          "name": "EMPLOYMENT_ID",
          "type": "int not null"
        },
        {
          "ordinal": 2,
          "name": "EMPLOYMENT_EXTERNAL_ID",
          "type": "char(40) null"
        },
        {
          "ordinal": 3,
          "name": "EMPLOYED_FROM_DATE",
          "type": "IncaDate null"
        },
        {
          "ordinal": 4,
          "name": "EMPLOYED_TO_DATE",
          "type": "IncaDate null"
        },
        {
          "ordinal": 5,
          "name": "COMPANY_OWNER_TYPE",
          "type": "smallint not null"
        },
        {
          "ordinal": 6,
          "name": "EMPLOYMENT_CHANGES_FROM_DATE",
          "type": "IncaDate null"
        },
        {
          "ordinal": 7,
          "name": "EMP_RPRTD_CHGS_TO_DATE",
          "type": "IncaDate null"
        },
        {
          "ordinal": 9,
          "name": "EMPLOYER",
          "type": "int not null"
        },
        {
          "ordinal": 10,
          "name": "EMPLOYEE",
          "type": "int not null"
        },
        {
          "ordinal": 11,
          "name": "CREATED_DATE",
          "type": "datetime not null"
        },
        {
          "ordinal": 12,
          "name": "CREATED_BY",
          "type": "varchar(40) not null"
        },
        {
          "ordinal": 13,
          "name": "LATEST_CHANGED_DATE",
          "type": "datetime null"
        },
        {
          "ordinal": 14,
          "name": "LATEST_CHANGED_BY",
          "type": "varchar(40) null"
        },
        {
          "ordinal": 15,
          "name": "FREE_TEXT_ID",
          "type": "int null"
        },
        {
          "ordinal": 16,
          "name": "EMPLOYMENT_STATUS",
          "type": "int not null"
        },
        {
          "ordinal": 17,
          "name": "EMPLOYMENT_ENDED_REASON",
          "type": "int null"
        },
        {
          "ordinal": 18,
          "name": "CREATE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 19,
          "name": "CHANGE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 20,
          "name": "EMP_RECALC_BLOCK_FROM_DATE",
          "type": "IncaDate null"
        },
        {
          "ordinal": 21,
          "name": "SYSTEM_DATE_END",
          "type": "bigint null"
        },
        {
          "ordinal": 22,
          "name": "ORIGINAL_EMPLOYED_FROM_DATE",
          "type": "IncaDate null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "INSTALLMENT_PLAN",
      "columns": [
        {
          "ordinal": 1,
          "name": "DEBT_CASE_ID",
          "type": "int not null"
        },
        {
          "ordinal": 2,
          "name": "START_DATE",
          "type": "IncaDate not null"
        },
        {
          "ordinal": 3,
          "name": "DURATION_IN_MONTHS",
          "type": "int not null"
        },
        {
          "ordinal": 4,
          "name": "SEND_NOTICES",
          "type": "bit not null"
        },
        {
          "ordinal": 5,
          "name": "NOTICE_FEES",
          "type": "bit not null"
        },
        {
          "ordinal": 6,
          "name": "SETUP_FEE",
          "type": "bit not null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "OUT_PAYMENT_PROVIDER_ATTRIBUTE",
      "columns": [
        {
          "ordinal": 1,
          "name": "OUT_PAYMENT_PROVIDER_ATTRIBUTE_ID",
          "type": "int not null"
        },
        {
          "ordinal": 2,
          "name": "OUT_PAYMENT_PROVIDER_ID",
          "type": "int not null"
        },
        {
          "ordinal": 3,
          "name": "OUT_PAYMENT_PROVIDER_LAST_SEND_DAYS",
          "type": "smallint not null"
        },
        {
          "ordinal": 4,
          "name": "OUT_PAYMENT_PROVIDER_FIRST_SEND_DAYS",
          "type": "smallint not null"
        },
        {
          "ordinal": 5,
          "name": "OUT_PAYMENT_PROVIDER_MIN_NUMBER_OF_PAYMENTS",
          "type": "int not null"
        },
        {
          "ordinal": 6,
          "name": "OUT_PAYMENT_PROVIDER_AGREEMENT_ID",
          "type": "varchar(40) not null"
        },
        {
          "ordinal": 7,
          "name": "COMPILE_START_TIME",
          "type": "datetime not null"
        },
        {
          "ordinal": 8,
          "name": "OUT_PAYMENT_SERVICE_ACCOUNT_NUMBER",
          "type": "char(40) null"
        },
        {
          "ordinal": 9,
          "name": "CREATE_ID",
          "type": "bigint not null"
        },
        {
          "ordinal": 10,
          "name": "CHANGE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 11,
          "name": "UPDATE_COUNTER",
          "type": "tinyint not null"
        },
        {
          "ordinal": 12,
          "name": "SYSTEM_DATE_START",
          "type": "bigint null"
        },
        {
          "ordinal": 13,
          "name": "SYSTEM_DATE_END",
          "type": "bigint null"
        },
        {
          "ordinal": 14,
          "name": "REPLACED_BY_ID",
          "type": "int null"
        },
        {
          "ordinal": 15,
          "name": "OUT_PAYMENT_PROVIDER_MAX_NUMBER_OF_PAYMENTS_PER_BUNDLE",
          "type": "int null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "PROCESS_TEMPLATE_VERSION_2",
      "columns": [
        {
          "ordinal": 1,
          "name": "PROCESS_TEMPLATE_VERSION_2_ID",
          "type": "int not null"
        },
        {
          "ordinal": 4,
          "name": "CREATE_ID",
          "type": "bigint not null"
        },
        {
          "ordinal": 5,
          "name": "CHANGE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 6,
          "name": "UPDATE_COUNTER",
          "type": "tinyint not null"
        },
        {
          "ordinal": 9,
          "name": "PROCESS_TEMPLATE_2_ID",
          "type": "int not null"
        },
        {
          "ordinal": 10,
          "name": "SYSTEM_DATE_START",
          "type": "bigint null"
        },
        {
          "ordinal": 11,
          "name": "SYSTEM_DATE_END",
          "type": "bigint null"
        },
        {
          "ordinal": 12,
          "name": "REPLACED_BY_ID",
          "type": "int null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "PROGNOSIS_PREMIUMS",
      "columns": [
        {
          "ordinal": 1,
          "name": "PROGNOSIS_PREMIUMS_ID",
          "type": "int not null"
        },
        {
          "ordinal": 2,
          "name": "PROGNOSIS_CONFIGURATION_DETAIL_ID",
          "type": "int not null"
        },
        {
          "ordinal": 3,
          "name": "INCLUDE_PROGNOSIS_PREMIUMS",
          "type": "int not null"
        },
        {
          "ordinal": 4,
          "name": "CREATE_ID",
          "type": "bigint not null"
        },
        {
          "ordinal": 5,
          "name": "CHANGE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 6,
          "name": "UPDATE_COUNTER",
          "type": "tinyint not null"
        },
        {
          "ordinal": 7,
          "name": "INCLUDE_CONTRACTUAL_TARGET_PRICE",
          "type": "bit null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "SUB_INS_IN_EMP_OPTS",
      "columns": [
        {
          "ordinal": 1,
          "name": "SUB_INS_IN_EMP_OPTS_ID",
          "type": "int not null"
        },
        {
          "ordinal": 2,
          "name": "INCLUDED_IN",
          "type": "int not null"
        },
        {
          "ordinal": 3,
          "name": "INS_PROD_IN_PPG_TERMS_BASE_ID",
          "type": "int not null"
        },
        {
          "ordinal": 4,
          "name": "INITIAL_ALLOCATION_PERCENTAGE",
          "type": "decimal(8,6) null"
        },
        {
          "ordinal": 5,
          "name": "INSURANCE_PRODUCT_SELECTED",
          "type": "smallint null"
        },
        {
          "ordinal": 6,
          "name": "CONTRACT_VERSION_EVENT_CAUSE",
          "type": "int null"
        },
        {
          "ordinal": 8,
          "name": "CREATE_ID",
          "type": "bigint not null"
        },
        {
          "ordinal": 9,
          "name": "CHANGE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 10,
          "name": "UPDATE_COUNTER",
          "type": "tinyint not null"
        },
        {
          "ordinal": 11,
          "name": "PROD_ALLCPRIN_IN_PPG_TERMS_ID",
          "type": "int null"
        },
        {
          "ordinal": 12,
          "name": "SORT_ORDER",
          "type": "smallint not null"
        }
      ]
    },
    {
      "schema": "dbo",
      "table": "TAX_INFORMATION_DETAIL_SE",
      "columns": [
        {
          "ordinal": 1,
          "name": "TAX_INFORMATION_DETAIL_SE_ID",
          "type": "int not null"
        },
        {
          "ordinal": 2,
          "name": "TAX_INFORMATION_ID",
          "type": "int not null"
        },
        {
          "ordinal": 3,
          "name": "TAX_TABLE_ID",
          "type": "int null"
        },
        {
          "ordinal": 4,
          "name": "CREATE_ID",
          "type": "bigint not null"
        },
        {
          "ordinal": 5,
          "name": "CHANGE_ID",
          "type": "bigint null"
        },
        {
          "ordinal": 6,
          "name": "UPDATE_COUNTER",
          "type": "tinyint not null"
        }
      ]
    }
  ]
}';

-- Render
DECLARE @rendered nvarchar(max) = dbo.fn_sisulate(@template, @bindings);

-- Inspect output
SELECT cast('<?generated -- ' + @rendered + ' --?>' as XML);

END -- end of proc
