-- Creates the least-privilege runtime login used by the Wabi Insight API.
-- Migrations run under a separate elevated login (db_owner); the runtime login cannot alter schema
-- and cannot UPDATE or DELETE append-only tables.
-- Replace the password with a secret from your deployment store before running.
USE [master];
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wabi_insight_app')
    CREATE LOGIN [wabi_insight_app] WITH PASSWORD = N'<replace-with-strong-secret>', CHECK_POLICY = ON;
GO
USE [WabiInsightDB];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wabi_insight_app')
    CREATE USER [wabi_insight_app] FOR LOGIN [wabi_insight_app];
GO
ALTER ROLE db_datareader ADD MEMBER [wabi_insight_app];
ALTER ROLE db_datawriter ADD MEMBER [wabi_insight_app];
GO
-- Append-only tables: the API may INSERT and SELECT only.
DENY UPDATE, DELETE ON dbo.AuditLogs TO [wabi_insight_app];
DENY UPDATE, DELETE ON dbo.UsageEvents TO [wabi_insight_app];
DENY UPDATE, DELETE ON dbo.SupportTicketEvents TO [wabi_insight_app];
-- Phase 14 ledgers: never edited; DELETE stays granted for the nightly retention purge (owner decision D3, 24 months).
DENY UPDATE ON dbo.AnalysisAccessFacts TO [wabi_insight_app];
DENY UPDATE ON dbo.RuleEvaluations TO [wabi_insight_app];
DENY UPDATE ON dbo.AlertEvents TO [wabi_insight_app];
GO
