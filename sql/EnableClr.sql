-- Enable CLR in this instance (requires sysadmin)
EXEC sp_configure 'show advanced options', 1; RECONFIGURE;
EXEC sp_configure 'clr enabled', 1; RECONFIGURE;
-- For MWE/dev only: relax strict security; for prod, sign assemblies instead
EXEC sp_configure 'clr strict security', 0; RECONFIGURE;
GO