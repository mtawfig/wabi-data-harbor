/* Wabi Bank sample — cross-system identifier check (read-only).
   The five systems are not integrated; they only reuse Personnel's identifiers. This script checks that every identifier
   column in the four other databases points at a real Personnel record (or at Core Banking for customer numbers):
     *UserId          -> Personnel SystemUsers.UserId, and the system's own Users table
     *EmployeeNumber  -> Personnel Employees.EmployeeNumber
     *DepartmentCode  -> Personnel Departments.DepartmentCode
     *SectionCode     -> Personnel Sections.SectionCode
     *BranchCode      -> Personnel Departments of type Branch
     *UnitCode        -> Personnel Departments
     CostCentreCode   -> Personnel Departments or Sections
     CIF (Digital)    -> Core Banking Customers.CIF
   and that each system's local Users row carries the same EmployeeNumber as Personnel for that UserId.
   Every Violations value must be 0. Run after 01-05: sqlcmd -S <server> -U <user> -P <password> -C -i 07-check-consistency.sql */
SET NOCOUNT ON;
DECLARE @checks TABLE (DatabaseName sysname, TableName sysname, ColumnName sysname, RuleName nvarchar(60), Violations int);
DECLARE @db sysname, @t sysname, @c sysname, @rule nvarchar(60), @target nvarchar(400), @sql nvarchar(max), @n int;
DECLARE @dbs TABLE (Name sysname);
INSERT INTO @dbs VALUES (N'WabiBankPersonnelDB'), (N'WabiBankCoreBankingDB'), (N'WabiBankFinanceDB'), (N'WabiBankDigitalDB'), (N'WabiBankITDB');
IF EXISTS (SELECT 1 FROM @dbs d WHERE DB_ID(d.Name) IS NULL)
BEGIN
    RAISERROR(N'Run 01-05 first: one or more Wabi Bank databases are missing.', 16, 1);
    RETURN;
END;

DECLARE @cols TABLE (DatabaseName sysname, TableName sysname, ColumnName sysname);
DECLARE dbc CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @dbs;
OPEN dbc; FETCH NEXT FROM dbc INTO @db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'SELECT @db, t.name, c.name FROM ' + QUOTENAME(@db) + N'.sys.tables t JOIN ' + QUOTENAME(@db) + N'.sys.columns c ON c.object_id = t.object_id
        WHERE t.schema_id = 1 AND (c.name LIKE N''%UserId'' OR c.name LIKE N''%EmployeeNumber'' OR c.name LIKE N''%DepartmentCode'' OR c.name LIKE N''%SectionCode''
           OR c.name LIKE N''%BranchCode'' OR c.name LIKE N''%UnitCode'' OR c.name IN (N''CostCentreCode'', N''CIF''))';
    INSERT INTO @cols EXEC sp_executesql @sql, N'@db sysname', @db = @db;
    FETCH NEXT FROM dbc INTO @db;
END;
CLOSE dbc; DEALLOCATE dbc;

DECLARE cc CURSOR LOCAL FAST_FORWARD FOR SELECT DatabaseName, TableName, ColumnName FROM @cols ORDER BY DatabaseName, TableName, ColumnName;
OPEN cc; FETCH NEXT FROM cc INTO @db, @t, @c;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @rule = NULL, @target = NULL;
    IF @c LIKE N'%UserId' SELECT @rule = N'UserId in Personnel SystemUsers', @target = N'SELECT UserId FROM WabiBankPersonnelDB.dbo.SystemUsers';
    ELSE IF @c LIKE N'%EmployeeNumber' SELECT @rule = N'EmployeeNumber in Personnel Employees', @target = N'SELECT EmployeeNumber FROM WabiBankPersonnelDB.dbo.Employees';
    ELSE IF @c LIKE N'%DepartmentCode' SELECT @rule = N'DepartmentCode in Personnel Departments', @target = N'SELECT DepartmentCode FROM WabiBankPersonnelDB.dbo.Departments';
    ELSE IF @c LIKE N'%SectionCode' SELECT @rule = N'SectionCode in Personnel Sections', @target = N'SELECT SectionCode FROM WabiBankPersonnelDB.dbo.Sections';
    ELSE IF @c LIKE N'%BranchCode' SELECT @rule = N'BranchCode is a Personnel branch', @target = N'SELECT DepartmentCode FROM WabiBankPersonnelDB.dbo.Departments WHERE DepartmentType = N''Branch''';
    ELSE IF @c LIKE N'%UnitCode' SELECT @rule = N'UnitCode in Personnel Departments', @target = N'SELECT DepartmentCode FROM WabiBankPersonnelDB.dbo.Departments';
    ELSE IF @c = N'CostCentreCode' SELECT @rule = N'CostCentreCode is a Personnel department or section',
        @target = N'SELECT DepartmentCode FROM WabiBankPersonnelDB.dbo.Departments UNION ALL SELECT SectionCode FROM WabiBankPersonnelDB.dbo.Sections';
    ELSE IF @c = N'CIF' AND @db <> N'WabiBankCoreBankingDB' SELECT @rule = N'CIF in Core Banking Customers', @target = N'SELECT CIF FROM WabiBankCoreBankingDB.dbo.Customers';
    IF @rule IS NOT NULL
    BEGIN
        SET @sql = N'SELECT @n = COUNT(*) FROM ' + QUOTENAME(@db) + N'.dbo.' + QUOTENAME(@t) + N' x WHERE x.' + QUOTENAME(@c) + N' IS NOT NULL AND x.' + QUOTENAME(@c)
                 + N' NOT IN (' + @target + N')';
        EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @n OUTPUT;
        INSERT INTO @checks VALUES (@db, @t, @c, @rule, @n);
        -- Actors must also be users of the system that recorded them.
        IF @c LIKE N'%UserId' AND @db <> N'WabiBankPersonnelDB' AND @t <> N'Users'
        BEGIN
            SET @sql = N'SELECT @n = COUNT(*) FROM ' + QUOTENAME(@db) + N'.dbo.' + QUOTENAME(@t) + N' x WHERE x.' + QUOTENAME(@c) + N' IS NOT NULL AND x.' + QUOTENAME(@c)
                     + N' NOT IN (SELECT UserId FROM ' + QUOTENAME(@db) + N'.dbo.Users)';
            EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @n OUTPUT;
            INSERT INTO @checks VALUES (@db, @t, @c, N'UserId in the system''s own Users', @n);
        END;
    END;
    FETCH NEXT FROM cc INTO @db, @t, @c;
END;
CLOSE cc; DEALLOCATE cc;

-- Local Users tables carry Personnel's EmployeeNumber for the same UserId (service accounts have none).
DECLARE uc CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @dbs WHERE Name <> N'WabiBankPersonnelDB';
OPEN uc; FETCH NEXT FROM uc INTO @db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'SELECT @n = COUNT(*) FROM ' + QUOTENAME(@db) + N'.dbo.Users u JOIN WabiBankPersonnelDB.dbo.SystemUsers s ON s.UserId = u.UserId
        WHERE ISNULL(s.EmployeeNumber, N'''') <> ISNULL(u.EmployeeNumber, N'''')';
    EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @n OUTPUT;
    INSERT INTO @checks VALUES (@db, N'Users', N'EmployeeNumber', N'Same EmployeeNumber as Personnel for the UserId', @n);
    FETCH NEXT FROM uc INTO @db;
END;
CLOSE uc; DEALLOCATE uc;

-- Digital channel transfers are posted once in Core Banking, from the same account, for the same amount.
INSERT INTO @checks
SELECT N'WabiBankDigitalDB', N'ChannelTransfers', N'CoreBankingReference', N'Completed transfer posted once in Core Banking', COUNT(*)
FROM WabiBankDigitalDB.dbo.ChannelTransfers t
OUTER APPLY (SELECT COUNT(*) AS Cnt, MAX(ABS(c.Amount)) AS Amount FROM WabiBankCoreBankingDB.dbo.Transactions c
             WHERE c.SourceReference = t.CoreBankingReference AND c.AccountNumber = t.FromAccountNumber) x
WHERE t.Status = N'Completed' AND (x.Cnt <> 1 OR x.Amount <> t.Amount);

SELECT DatabaseName, TableName, ColumnName, RuleName, Violations FROM @checks ORDER BY Violations DESC, DatabaseName, TableName, ColumnName, RuleName;
SELECT COUNT(*) AS ChecksRun, SUM(CASE WHEN Violations > 0 THEN 1 ELSE 0 END) AS [Failed checks (must be 0)] FROM @checks;
