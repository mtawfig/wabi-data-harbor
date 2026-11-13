using WabiInsight.Core.Entities.Insights;

namespace WabiInsight.Infrastructure.Insights;

/// <summary>
/// The tenant-scoped <c>insights</c> views (specification §12.1) and the initial global catalog. Every view
/// exposes <c>OrganizationId</c> and only counts, codes, statuses and timestamps: no names of people, no
/// e-mail addresses, no subjects or descriptions, no secrets, no prompt text. The migration creates the views
/// from this list so the SQL lives in one reviewed place.
/// </summary>
public static class InsightsSchema
{
    public const string SchemaName = "insights";

    /// <summary>
    /// Gives the <c>insights</c> schema the owner of <c>dbo</c>, so the views and the tables they read have one owner
    /// and a login with SELECT on the schema alone can read the views (migration AlignInsightsSchemaOwner).
    /// Changing a schema's owner drops the permissions granted on it, so they are read first and granted again.
    /// Does nothing when the owners already match. Safe to run again.
    /// </summary>
    public const string AlignOwnerSql = """
        IF SCHEMA_ID(N'insights') IS NOT NULL
           AND (SELECT principal_id FROM sys.schemas WHERE name = N'insights') <> (SELECT principal_id FROM sys.schemas WHERE name = N'dbo')
        BEGIN
            DECLARE @permissions nvarchar(max) = N'';
            SELECT @permissions = @permissions
                + CASE p.state WHEN 'D' THEN N'DENY ' ELSE N'GRANT ' END + p.permission_name
                + N' ON SCHEMA::[insights] TO ' + QUOTENAME(u.name)
                + CASE p.state WHEN 'W' THEN N' WITH GRANT OPTION' ELSE N'' END + N'; '
            FROM sys.database_permissions p
            JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
            WHERE p.class = 3 AND p.major_id = SCHEMA_ID(N'insights');

            DECLARE @owner sysname = (SELECT USER_NAME(principal_id) FROM sys.schemas WHERE name = N'dbo');
            DECLARE @transfer nvarchar(400) = N'ALTER AUTHORIZATION ON SCHEMA::[insights] TO ' + QUOTENAME(@owner) + N';';
            EXEC sys.sp_executesql @transfer;
            IF LEN(@permissions) > 0 EXEC sys.sp_executesql @permissions;
        END
        """;

    public static readonly IReadOnlyList<(string ViewName, string Sql)> Views =
    [
        ("Users", """
            SELECT u.OrganizationId, u.UserId, u.Status, u.DepartmentId, d.DepartmentCode, u.SectionId, s.SectionCode,
                   u.PreferredLanguage, u.CreatedAt, u.LastLoginAt,
                   CAST(CASE WHEN u.Status = 'Active' THEN 1 ELSE 0 END AS bit) AS IsActive
            FROM dbo.OrganizationUsers u
            LEFT JOIN dbo.Departments d ON d.DepartmentId = u.DepartmentId
            LEFT JOIN dbo.Sections s ON s.SectionId = u.SectionId
            """),
        ("Departments", """
            SELECT d.OrganizationId, d.DepartmentId, d.DepartmentCode, d.DepartmentNameEn, d.DepartmentNameAr, d.IsActive, d.CreatedAt,
                   (SELECT COUNT_BIG(*) FROM dbo.OrganizationUsers u WHERE u.DepartmentId = d.DepartmentId) AS UserCount
            FROM dbo.Departments d
            """),
        ("Sections", """
            SELECT s.OrganizationId, s.SectionId, s.DepartmentId, d.DepartmentCode, s.SectionCode, s.SectionNameEn, s.SectionNameAr, s.IsActive, s.CreatedAt,
                   (SELECT COUNT_BIG(*) FROM dbo.OrganizationUsers u WHERE u.SectionId = s.SectionId) AS UserCount
            FROM dbo.Sections s
            INNER JOIN dbo.Departments d ON d.DepartmentId = s.DepartmentId
            """),
        ("UsageEvents", """
            SELECT e.OrganizationId, e.UsageEventId, e.EventType, e.EntityType, e.ActorType, e.ActorId, e.DepartmentId,
                   e.Quantity, e.Succeeded, e.CreatedAt, CAST(e.CreatedAt AS date) AS EventDate
            FROM dbo.UsageEvents e
            WHERE e.OrganizationId IS NOT NULL
            """),
        ("DailyUsage", """
            SELECT e.OrganizationId, CAST(e.CreatedAt AS date) AS EventDate, e.EventType, e.DepartmentId,
                   COUNT_BIG(*) AS EventCount,
                   SUM(CASE WHEN e.Succeeded = 1 THEN 1 ELSE 0 END) AS SucceededCount,
                   SUM(CASE WHEN e.Succeeded = 1 THEN 0 ELSE 1 END) AS FailedCount,
                   SUM(e.Quantity) AS TotalQuantity,
                   COUNT_BIG(DISTINCT e.ActorId) AS ActiveActors
            FROM dbo.UsageEvents e
            WHERE e.OrganizationId IS NOT NULL
            GROUP BY e.OrganizationId, CAST(e.CreatedAt AS date), e.EventType, e.DepartmentId
            """),
        ("SupportTickets", """
            SELECT t.OrganizationId, t.TicketId, t.ReferenceNumber, t.TicketType, t.Impact, t.Status, t.Category,
                   t.RequiresSupportAccess, t.ReopenedCount, t.CreatedAt, t.FirstOpenedAt, t.StartedAt, t.ResolvedAt, t.CancelledAt, t.LastActivityAt,
                   DATEDIFF(minute, t.CreatedAt, t.FirstOpenedAt) AS FirstResponseMinutes,
                   DATEDIFF(minute, t.CreatedAt, t.ResolvedAt) AS ResolutionMinutes
            FROM dbo.SupportTickets t
            """),
        ("SupportAccessGrants", """
            SELECT g.OrganizationId, g.GrantId, g.TicketId, g.Status, g.CreatedAt, g.ApprovedAt, g.ExpiresAt, g.RevokedAt, g.CompletedAt
            FROM dbo.SupportAccessGrants g
            """),
        ("Applications", """
            SELECT a.OrganizationId, a.ApplicationId, a.ApplicationCode, a.ApplicationName, a.ApplicationNameAr, a.DatabaseType,
                   a.IsBuiltIn, a.IsActive, a.IsApproved, a.CreatedAt, a.LastConnectionTestAt, a.LastConnectionTestSucceeded, a.LastMetadataRefreshAt,
                   (SELECT COUNT_BIG(*) FROM dbo.DatabaseObjects o WHERE o.ApplicationId = a.ApplicationId AND o.IsPresent = 1) AS ObjectCount,
                   (SELECT COUNT_BIG(*) FROM dbo.DatabaseObjects o WHERE o.ApplicationId = a.ApplicationId AND o.IsPresent = 1 AND o.IsApproved = 1) AS ApprovedObjectCount
            FROM dbo.Applications a
            """),
        ("EmailMessages", """
            SELECT m.OrganizationId, m.EmailMessageId, m.TemplateCode, m.Language, m.Status, m.Attempts, m.CreatedAt, m.SentAt
            FROM dbo.EmailMessages m
            WHERE m.OrganizationId IS NOT NULL
            """),
    ];

    public sealed record SeedColumn(string Name, string En, string Ar);

    public sealed record SeedObject(string ViewName, string En, string Ar, string Description, int SortOrder, IReadOnlyList<SeedColumn> Columns);

    public sealed record SeedMetric(string Code, string ViewName, string En, string Ar, string Description, MetricAggregate Aggregate, string? Column, string? DateColumn, string FiltersJson, string Unit, int SortOrder);

    /// <summary>EN/AR labels of the views and columns as shipped; later syncs only add new columns (unapproved).</summary>
    public static readonly IReadOnlyList<SeedObject> Objects =
    [
        new("Users", "Users", "المستخدمون", "One row per organization user: status, department, section, language, creation and last login. No names or e-mail addresses.", 10,
        [
            new("UserId", "User id", "معرّف المستخدم"), new("Status", "Status", "الحالة"), new("DepartmentId", "Department id", "معرّف الإدارة"), new("DepartmentCode", "Department code", "رمز الإدارة"),
            new("SectionId", "Section id", "معرّف القسم"), new("SectionCode", "Section code", "رمز القسم"), new("PreferredLanguage", "Preferred language", "اللغة المفضلة"),
            new("CreatedAt", "Created at", "تاريخ الإنشاء"), new("LastLoginAt", "Last login", "آخر تسجيل دخول"), new("IsActive", "Is active", "نشط"),
        ]),
        new("Departments", "Departments", "الإدارات", "Departments with their user counts.", 20,
        [
            new("DepartmentId", "Department id", "معرّف الإدارة"), new("DepartmentCode", "Department code", "رمز الإدارة"), new("DepartmentNameEn", "Name (EN)", "الاسم (إنجليزي)"), new("DepartmentNameAr", "Name (AR)", "الاسم (عربي)"),
            new("IsActive", "Is active", "نشطة"), new("CreatedAt", "Created at", "تاريخ الإنشاء"), new("UserCount", "Users", "عدد المستخدمين"),
        ]),
        new("Sections", "Sections", "الأقسام", "Sections with their department and user counts.", 30,
        [
            new("SectionId", "Section id", "معرّف القسم"), new("DepartmentId", "Department id", "معرّف الإدارة"), new("DepartmentCode", "Department code", "رمز الإدارة"), new("SectionCode", "Section code", "رمز القسم"),
            new("SectionNameEn", "Name (EN)", "الاسم (إنجليزي)"), new("SectionNameAr", "Name (AR)", "الاسم (عربي)"), new("IsActive", "Is active", "نشط"), new("CreatedAt", "Created at", "تاريخ الإنشاء"), new("UserCount", "Users", "عدد المستخدمين"),
        ]),
        new("UsageEvents", "Usage events", "أحداث الاستخدام", "Verified platform events of the organization: logins, analyses, reports, e-mails, tickets. One row per event; never AI output.", 40,
        [
            new("UsageEventId", "Event id", "معرّف الحدث"), new("EventType", "Event type", "نوع الحدث"), new("EntityType", "Entity type", "نوع الكيان"), new("ActorType", "Actor type", "نوع الفاعل"),
            new("ActorId", "Actor id", "معرّف الفاعل"), new("DepartmentId", "Department id", "معرّف الإدارة"), new("Quantity", "Quantity", "الكمية"), new("Succeeded", "Succeeded", "نجح"),
            new("CreatedAt", "Occurred at", "وقت الحدوث"), new("EventDate", "Event date", "تاريخ الحدث"),
        ]),
        new("DailyUsage", "Daily usage", "الاستخدام اليومي", "Usage events aggregated per day, event type and department: counts, failures, quantities and distinct actors.", 50,
        [
            new("EventDate", "Event date", "تاريخ الحدث"), new("EventType", "Event type", "نوع الحدث"), new("DepartmentId", "Department id", "معرّف الإدارة"), new("EventCount", "Events", "عدد الأحداث"),
            new("SucceededCount", "Succeeded", "الناجحة"), new("FailedCount", "Failed", "الفاشلة"), new("TotalQuantity", "Total quantity", "إجمالي الكمية"), new("ActiveActors", "Active actors", "الفاعلون النشطون"),
        ]),
        new("SupportTickets", "Support tickets", "تذاكر الدعم", "Tickets by type, impact and status with response and resolution times. No subjects or descriptions.", 60,
        [
            new("TicketId", "Ticket id", "معرّف التذكرة"), new("ReferenceNumber", "Reference", "المرجع"), new("TicketType", "Type", "النوع"), new("Impact", "Impact", "الأثر"), new("Status", "Status", "الحالة"),
            new("Category", "Category", "الفئة"), new("RequiresSupportAccess", "Requires support access", "تتطلب وصول الدعم"), new("ReopenedCount", "Reopened", "مرات إعادة الفتح"),
            new("CreatedAt", "Created at", "تاريخ الإنشاء"), new("FirstOpenedAt", "First opened at", "أول فتح"), new("StartedAt", "Started at", "بدء العمل"), new("ResolvedAt", "Resolved at", "تاريخ الحل"),
            new("CancelledAt", "Cancelled at", "تاريخ الإلغاء"), new("LastActivityAt", "Last activity", "آخر نشاط"), new("FirstResponseMinutes", "First response (minutes)", "أول استجابة (دقائق)"), new("ResolutionMinutes", "Resolution (minutes)", "الحل (دقائق)"),
        ]),
        new("SupportAccessGrants", "Support access grants", "منح وصول الدعم", "Scoped support-access grants and their lifecycle timestamps.", 70,
        [
            new("GrantId", "Grant id", "معرّف المنحة"), new("TicketId", "Ticket id", "معرّف التذكرة"), new("Status", "Status", "الحالة"), new("CreatedAt", "Proposed at", "تاريخ الاقتراح"),
            new("ApprovedAt", "Approved at", "تاريخ الموافقة"), new("ExpiresAt", "Expires at", "تاريخ الانتهاء"), new("RevokedAt", "Revoked at", "تاريخ الإلغاء"), new("CompletedAt", "Completed at", "تاريخ الإكمال"),
        ]),
        new("Applications", "Applications", "التطبيقات", "Configured applications: type, approval, last test and refresh, object counts. Never connection details.", 80,
        [
            new("ApplicationId", "Application id", "معرّف التطبيق"), new("ApplicationCode", "Code", "الرمز"), new("ApplicationName", "Name (EN)", "الاسم (إنجليزي)"), new("ApplicationNameAr", "Name (AR)", "الاسم (عربي)"),
            new("DatabaseType", "Database type", "نوع قاعدة البيانات"), new("IsBuiltIn", "Built-in", "مدمج"), new("IsActive", "Is active", "نشط"), new("IsApproved", "Is approved", "معتمد"), new("CreatedAt", "Created at", "تاريخ الإنشاء"),
            new("LastConnectionTestAt", "Last connection test", "آخر اختبار اتصال"), new("LastConnectionTestSucceeded", "Last test succeeded", "نجح آخر اختبار"), new("LastMetadataRefreshAt", "Last metadata refresh", "آخر تحديث للبيانات الوصفية"),
            new("ObjectCount", "Objects", "عدد الكائنات"), new("ApprovedObjectCount", "Approved objects", "الكائنات المعتمدة"),
        ]),
        new("EmailMessages", "E-mail messages", "رسائل البريد", "Outbox entries by template, language and delivery status. No recipients or content.", 90,
        [
            new("EmailMessageId", "Message id", "معرّف الرسالة"), new("TemplateCode", "Template", "القالب"), new("Language", "Language", "اللغة"), new("Status", "Status", "الحالة"),
            new("Attempts", "Attempts", "المحاولات"), new("CreatedAt", "Queued at", "وقت الإدراج"), new("SentAt", "Sent at", "وقت الإرسال"),
        ]),
    ];

    private const string LoginFilter = """[{"column":"EventType","operator":"eq","values":["Auth.Login"]}]""";

    /// <summary>Metrics available from the data that exists today (specification §12.3); AI, report and sharing metrics arrive with their phases.</summary>
    public static readonly IReadOnlyList<SeedMetric> Metrics =
    [
        new("users.total", "Users", "Total users", "إجمالي المستخدمين", "All organization users regardless of status.", MetricAggregate.Count, null, "CreatedAt", "[]", "count", 10),
        new("users.active", "Users", "Active users", "المستخدمون النشطون", "Users whose status is Active.", MetricAggregate.Count, null, null, """[{"column":"Status","operator":"eq","values":["Active"]}]""", "count", 20),
        new("users.loggedIn", "UsageEvents", "Users who signed in", "المستخدمون الذين سجّلوا الدخول", "Distinct users with at least one successful login in the period.", MetricAggregate.CountDistinct, "ActorId", "CreatedAt", LoginFilter, "count", 30),
        new("logins.total", "UsageEvents", "Logins", "تسجيلات الدخول", "Successful logins in the period.", MetricAggregate.Count, null, "CreatedAt", LoginFilter, "count", 40),
        new("logins.failed", "UsageEvents", "Failed logins", "محاولات الدخول الفاشلة", "Failed login attempts in the period.", MetricAggregate.Count, null, "CreatedAt", """[{"column":"EventType","operator":"eq","values":["Auth.LoginFailed"]}]""", "count", 50),
        new("usage.events", "UsageEvents", "Usage events", "أحداث الاستخدام", "All verified events in the period; group by event type or department for the breakdown.", MetricAggregate.Count, null, "CreatedAt", "[]", "count", 60),
        new("usage.failedOperations", "UsageEvents", "Failed operations", "العمليات الفاشلة", "Events recorded as failed (API errors, failed e-mails, failed queries).", MetricAggregate.Count, null, "CreatedAt", """[{"column":"Succeeded","operator":"eq","values":["false"]}]""", "count", 70),
        new("tickets.total", "SupportTickets", "Support tickets", "تذاكر الدعم", "Tickets created in the period; group by status, type or impact.", MetricAggregate.Count, null, "CreatedAt", "[]", "count", 80),
        new("tickets.open", "SupportTickets", "Open tickets", "التذاكر المفتوحة", "Tickets not yet resolved or cancelled.", MetricAggregate.Count, null, null, """[{"column":"Status","operator":"in","values":["New","Read","Started","PendingUserResponse"]}]""", "count", 90),
        new("tickets.avgFirstResponseMinutes", "SupportTickets", "Average first response (minutes)", "متوسط أول استجابة (دقائق)", "Average minutes from creation to first platform view.", MetricAggregate.Average, "FirstResponseMinutes", "CreatedAt", "[]", "minutes", 100),
        new("tickets.avgResolutionMinutes", "SupportTickets", "Average resolution (minutes)", "متوسط الحل (دقائق)", "Average minutes from creation to resolution.", MetricAggregate.Average, "ResolutionMinutes", "CreatedAt", """[{"column":"Status","operator":"eq","values":["Resolved"]}]""", "minutes", 110),
        new("supportAccess.grants", "SupportAccessGrants", "Support access grants", "منح وصول الدعم", "Grants proposed in the period; group by status.", MetricAggregate.Count, null, "CreatedAt", "[]", "count", 120),
        new("applications.configured", "Applications", "Configured applications", "التطبيقات المهيأة", "Active applications of the organization, including the built-in one.", MetricAggregate.Count, null, null, """[{"column":"IsActive","operator":"eq","values":["true"]}]""", "count", 130),
        new("applications.approvedObjects", "Applications", "Approved objects", "الكائنات المعتمدة", "Objects approved for querying across applications.", MetricAggregate.Sum, "ApprovedObjectCount", null, "[]", "count", 140),
        new("emails.sent", "EmailMessages", "E-mails sent", "الرسائل المرسلة", "Messages delivered in the period.", MetricAggregate.Count, null, "CreatedAt", """[{"column":"Status","operator":"eq","values":["Sent"]}]""", "count", 150),
        new("emails.failed", "EmailMessages", "E-mails failed", "الرسائل الفاشلة", "Messages that could not be delivered.", MetricAggregate.Count, null, "CreatedAt", """[{"column":"Status","operator":"eq","values":["Failed"]}]""", "count", 160),
        new("departments.active", "Departments", "Active departments", "الإدارات النشطة", "Departments currently active.", MetricAggregate.Count, null, null, """[{"column":"IsActive","operator":"eq","values":["true"]}]""", "count", 170),
    ];
}
