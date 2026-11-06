-- The app's rights, given to the itsm_app role; 3-app-account.sql adds the app's account to it.
-- The app may read and change data but not the tables themselves. Records the app keeps append-only (audit log,
-- ticket and risk history, sign-in events, security incident records and evidence) may be added, never changed
-- or deleted, so not even the app's own account can rewrite history.
-- scripts/publish.ps1 adds this to the end of 2-database-update.sql, so new tables are covered on every release.

IF DATABASE_PRINCIPAL_ID(N'itsm_app') IS NULL
    CREATE ROLE [itsm_app];
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[dbo] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[SystemAuditLogs] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[TicketHistories] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[AuthEvents] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[RiskHistory] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[SecurityIncidentActions] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[SecurityIncidentAccesses] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[SecurityEvidence] TO [itsm_app];
DENY UPDATE, DELETE ON [dbo].[SecurityEvidenceCustody] TO [itsm_app];
-- The app only reads which updates have been applied
DENY INSERT, UPDATE, DELETE ON [dbo].[__EFMigrationsHistory] TO [itsm_app];
GO
