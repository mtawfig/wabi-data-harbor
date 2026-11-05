-- Creates the dedicated read-only login used for Platform Insights queries (specification §12.1).
-- It can SELECT from the [insights] schema only: never the raw dbo tables, never secrets or prompt text.
-- Put its connection string in the secret setting Insights:ConnectionString (appsettings.Local.json,
-- user-secrets or the Insights__ConnectionString environment variable). Replace the password first.
USE [master];
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wabi_insight_insights')
    CREATE LOGIN [wabi_insight_insights] WITH PASSWORD = N'<replace-with-strong-secret>', CHECK_POLICY = ON;
GO
USE [WabiInsight];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wabi_insight_insights')
    CREATE USER [wabi_insight_insights] FOR LOGIN [wabi_insight_insights];
GO
-- SELECT on the insights schema only. The views are created by the AddPlatformInsights migration.
GRANT SELECT ON SCHEMA::[insights] TO [wabi_insight_insights];
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[dbo] TO [wabi_insight_insights];
GO
-- Verify: the API reports "confined to the insights schema" on the Platform Insights Configuration page.
