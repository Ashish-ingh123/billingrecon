using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The migrations, checked without a database: they exist under their names, they are complete (the model has no
/// change that is missing from them), and the script they produce creates the Tenants, Patients, Payers,
/// ServiceItems, Bills and BillLines tables as documented. Applying them to a real SQL Server is covered by the Testcontainers tests.
/// </summary>
public sealed class MigrationsTests : IDisposable
{
    private const string HowToAdd =
        "Run: dotnet ef migrations add <name> --project src/BillingRecon.Infrastructure --output-dir Common/Migrations (docs/runbook.md, section 3).";

    private readonly BillingReconDbContext _context = TestContexts.CreateProduction();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void The_first_migration_is_0001_Tenants()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.ShouldNotBeEmpty($"There is no migration yet. {HowToAdd.Replace("<name>", "0001_Tenants", StringComparison.Ordinal)}");
        migrations[0].ShouldEndWith("_0001_Tenants");
    }

    [Fact]
    public void The_second_migration_is_0002_SyncCurrentModel()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        // The Patients and Payers migration. It was meant to be 0002_Payers and was scaffolded as
        // 0002_SyncCurrentModel; a migration is never renamed afterwards (docs/database.md, section 8).
        migrations.Length.ShouldBeGreaterThanOrEqualTo(2, $"The Patients and Payers migration is missing. {HowToAdd.Replace("<name>", "0002_SyncCurrentModel", StringComparison.Ordinal)}");
        migrations[1].ShouldBe("20261018182819_0002_SyncCurrentModel");
    }

    [Fact]
    public void The_third_migration_is_0003_ServiceItems()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(3, $"The ServiceItems migration is missing. {HowToAdd.Replace("<name>", "0003_ServiceItems", StringComparison.Ordinal)}");
        migrations[2].ShouldEndWith("_0003_ServiceItems");
    }

    [Fact]
    public void The_fourth_migration_is_0004_Bills()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(4, $"The Bills migration is missing. {HowToAdd.Replace("<name>", "0004_Bills", StringComparison.Ordinal)}");
        migrations[3].ShouldEndWith("_0004_Bills");
    }

    [Fact]
    public void The_fifth_migration_is_0005_NumberSeries()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(5, $"The NumberSeries migration is missing. {HowToAdd.Replace("<name>", "0005_NumberSeries", StringComparison.Ordinal)}");
        migrations[4].ShouldEndWith("_0005_NumberSeries");
    }

    [Fact]
    public void The_migrations_create_the_number_series_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [NumberSeries]", customMessage: $"The migrations do not create the NumberSeries table. {HowToAdd}");
        script.ShouldContain("[DocumentType] varchar(20) NOT NULL", Case.Sensitive);
        script.ShouldContain("[FiscalYear] char(7) NOT NULL", Case.Sensitive);
        script.ShouldContain("[Prefix] varchar(3) NOT NULL", Case.Sensitive);
        script.ShouldContain("[NextNumber] int NOT NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_NumberSeries_DocumentType] CHECK ([DocumentType] IN ('Bill', 'CreditNote', 'Payment', 'Adjustment', 'Refund'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_NumberSeries_NextNumber] CHECK ([NextNumber] >= 1)", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_NumberSeries_Prefix] CHECK (LEN([Prefix]) BETWEEN 1 AND 3 AND [Prefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2)", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_NumberSeries_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_NumberSeries_TenantId_DocumentType_FiscalYear] ON [NumberSeries] ([TenantId], [DocumentType], [FiscalYear])", Case.Sensitive);
    }

    [Fact]
    public void The_sixth_migration_is_0006_IdempotencyKeys()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(6, $"The IdempotencyKeys migration is missing. {HowToAdd.Replace("<name>", "0006_IdempotencyKeys", StringComparison.Ordinal)}");
        migrations[5].ShouldEndWith("_0006_IdempotencyKeys");
    }

    [Fact]
    public void The_migrations_create_the_idempotency_keys_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [IdempotencyKeys]", customMessage: $"The migrations do not create the IdempotencyKeys table. {HowToAdd}");
        script.ShouldContain("[Key] uniqueidentifier NOT NULL", Case.Sensitive);
        script.ShouldContain("[UserId] uniqueidentifier NULL", Case.Sensitive);
        script.ShouldContain("[Method] varchar(8) NOT NULL", Case.Sensitive);
        script.ShouldContain("[Path] nvarchar(400) NOT NULL", Case.Sensitive);
        script.ShouldContain("[RequestHash] binary(32) NOT NULL", Case.Sensitive);
        script.ShouldContain("[Status] varchar(12) NOT NULL", Case.Sensitive);
        script.ShouldContain("[StatusCode] int NULL", Case.Sensitive);
        script.ShouldContain("[ResponseContentType] varchar(100) NULL", Case.Sensitive);
        script.ShouldContain("[ResponseHeaders] nvarchar(1000) NULL", Case.Sensitive);
        script.ShouldContain("[ResponseBody] nvarchar(max) NULL", Case.Sensitive);
        script.ShouldContain("[ExpiresAtUtc] datetimeoffset(3) NOT NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [PK_IdempotencyKeys] PRIMARY KEY ([TenantId], [Key])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_IdempotencyKeys_Status] CHECK ([Status] IN ('InProgress', 'Completed'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_IdempotencyKeys_Completed] CHECK (", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_IdempotencyKeys_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_IdempotencyKeys_TenantId_ExpiresAtUtc] ON [IdempotencyKeys] ([TenantId], [ExpiresAtUtc])", Case.Sensitive);
    }

    [Fact]
    public void The_seventh_migration_is_0007_Audit()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(7, $"The Audit migration is missing. {HowToAdd.Replace("<name>", "0007_Audit", StringComparison.Ordinal)}");
        migrations[6].ShouldEndWith("_0007_Audit");
    }

    [Fact]
    public void The_migrations_create_the_audit_records_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [AuditRecords]", customMessage: $"The migrations do not create the AuditRecords table. {HowToAdd}");
        script.ShouldContain("[Id] bigint NOT NULL IDENTITY", Case.Sensitive);
        script.ShouldContain("[OccurredAtUtc] datetimeoffset(3) NOT NULL", Case.Sensitive);
        script.ShouldContain("[CorrelationId] uniqueidentifier NOT NULL", Case.Sensitive);
        script.ShouldContain("[EntityType] varchar(50) NOT NULL", Case.Sensitive);
        script.ShouldContain("[EntityId] uniqueidentifier NOT NULL", Case.Sensitive);
        script.ShouldContain("[DocumentNumber] varchar(30) NULL", Case.Sensitive);
        script.ShouldContain("[Action] varchar(40) NOT NULL", Case.Sensitive);
        script.ShouldContain("[BeforeJson] nvarchar(max) NULL", Case.Sensitive);
        script.ShouldContain("[AfterJson] nvarchar(max) NOT NULL", Case.Sensitive);
        script.ShouldContain("[Reason] nvarchar(500) NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [PK_AuditRecords] PRIMARY KEY ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_AuditRecords_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_AuditRecords_TenantId_OccurredAtUtc] ON [AuditRecords] ([TenantId], [OccurredAtUtc])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_AuditRecords_TenantId_CorrelationId] ON [AuditRecords] ([TenantId], [CorrelationId])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_AuditRecords_TenantId_EntityType_EntityId_Id] ON [AuditRecords] ([TenantId], [EntityType], [EntityId], [Id])", Case.Sensitive);
    }

    [Fact]
    public void The_eighth_migration_is_0008_Receivables()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(8, $"The Receivables migration is missing. {HowToAdd.Replace("<name>", "0008_Receivables", StringComparison.Ordinal)}");
        migrations[7].ShouldEndWith("_0008_Receivables");
    }

    [Fact]
    public void The_migrations_create_the_ledger_and_the_payer_balances_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [LedgerPostings]", customMessage: $"The migrations do not create the LedgerPostings table. {HowToAdd}");
        script.ShouldContain("CREATE TABLE [LedgerEntries]", customMessage: $"The migrations do not create the LedgerEntries table. {HowToAdd}");
        script.ShouldContain("CREATE TABLE [PayerBalances]", customMessage: $"The migrations do not create the PayerBalances table. {HowToAdd}");
        script.ShouldContain("[Kind] varchar(30) NOT NULL", Case.Sensitive);
        script.ShouldContain("[BusinessDate] date NOT NULL", Case.Sensitive);
        script.ShouldContain("[Description] nvarchar(300) NOT NULL", Case.Sensitive);
        script.ShouldContain("[ReversalOfPostingId] uniqueidentifier NULL", Case.Sensitive);
        script.ShouldContain("[PostedByUserId] uniqueidentifier NULL", Case.Sensitive);
        script.ShouldContain("[LineNo] smallint NOT NULL", Case.Sensitive);
        script.ShouldContain("[Account] varchar(20) NOT NULL", Case.Sensitive);
        script.ShouldContain("[SubjectType] varchar(20) NULL", Case.Sensitive);
        script.ShouldContain("[Debit] decimal(18,2) NOT NULL", Case.Sensitive);
        script.ShouldContain("[Credit] decimal(18,2) NOT NULL", Case.Sensitive);
        script.ShouldContain("[NetOutstanding] AS [Receivable] - [UnappliedCash] - [CreditAvailable] PERSISTED", Case.Sensitive);
        script.ShouldNotContain("LastPostingId");
        script.ShouldContain("CONSTRAINT [CK_LedgerPostings_Kind] CHECK ([Kind] IN ('BillPosted', 'PaymentRecorded', 'PaymentAllocated', 'AllocationReversed', 'PaymentReversed', 'CreditNotePosted', 'CreditApplied', 'CreditApplicationReversed', 'AdjustmentPosted', 'RefundPosted'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_LedgerPostings_DocumentType] CHECK ([DocumentType] IN ('Bill', 'Payment', 'PaymentAllocation', 'CreditNote', 'CreditNoteAllocation', 'Adjustment', 'Refund'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_LedgerEntries_Account] CHECK ([Account] IN ('Receivable', 'UnappliedCash', 'CreditAvailable', 'Revenue', 'Cash', 'Adjustments'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_LedgerEntries_OneSide] CHECK (([Debit] > 0 AND [Credit] = 0) OR ([Debit] = 0 AND [Credit] > 0))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_LedgerEntries_Subject] CHECK (([Account] = 'Receivable' AND [SubjectType] = 'Bill'", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_PayerBalances_Receivable] CHECK ([Receivable] >= 0)", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [PK_PayerBalances] PRIMARY KEY ([PayerId])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_PayerBalances_PayerId] FOREIGN KEY ([PayerId]) REFERENCES [Payers] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_LedgerPostings_PayerId] FOREIGN KEY ([PayerId]) REFERENCES [Payers] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_LedgerPostings_ReversalOfPostingId] FOREIGN KEY ([ReversalOfPostingId]) REFERENCES [LedgerPostings] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_LedgerEntries_PayerId] FOREIGN KEY ([PayerId]) REFERENCES [Payers] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_LedgerEntries_PostingId] FOREIGN KEY ([PostingId]) REFERENCES [LedgerPostings] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_LedgerEntries_TenantId_PostingId_LineNo] ON [LedgerEntries] ([TenantId], [PostingId], [LineNo])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_LedgerEntries_TenantId_SubjectType_SubjectId] ON [LedgerEntries] ([TenantId], [SubjectType], [SubjectId]) INCLUDE ([Account], [Debit], [Credit])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_LedgerEntries_TenantId_PayerId_Account] ON [LedgerEntries] ([TenantId], [PayerId], [Account]) INCLUDE ([Debit], [Credit])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_LedgerPostings_TenantId_DocumentType_DocumentId] ON [LedgerPostings] ([TenantId], [DocumentType], [DocumentId])", Case.Sensitive);
    }

    [Fact]
    public void BR404_the_receivables_migration_opens_the_balances_of_every_payer_that_already_exists()
    {
        // The one statement a scaffold cannot write. It is added by hand at the end of Up() of 0008_Receivables
        // (ADR 0021; docs/runbook.md section 8 has the line to paste). Without it a payer created before the
        // migration has no row of balances, and the first posting for it fails. WHERE NOT EXISTS makes it safe to
        // run on a database where some payers have the row already.
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain(
            "INSERT INTO [PayerBalances] ([PayerId], [TenantId], [Receivable], [UnappliedCash], [CreditAvailable], [UpdatedAtUtc]) SELECT p.[Id], p.[TenantId], 0, 0, 0, SYSUTCDATETIME() FROM [Payers] AS p WHERE NOT EXISTS (SELECT 1 FROM [PayerBalances] AS b WHERE b.[PayerId] = p.[Id])",
            Case.Sensitive,
            "0008_Receivables lacks the statement that opens the balances of existing payers. Paste the line from docs/runbook.md section 8 at the end of its Up method.");
    }

    [Fact]
    public void The_ninth_migration_is_0009_BillPosting()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(9, $"The BillPosting migration is missing. {HowToAdd.Replace("<name>", "0009_BillPosting", StringComparison.Ordinal)}");
        migrations[8].ShouldEndWith("_0009_BillPosting");
    }

    [Fact]
    public void The_migrations_give_the_bills_table_the_number_and_the_constraints_of_a_posted_bill()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("ALTER TABLE [Bills] ADD [Number] varchar(16) NULL", Case.Sensitive, $"The migrations do not add the number to Bills. {HowToAdd}");
        script.ShouldContain("ALTER TABLE [Bills] ADD [FiscalYear] char(7) NULL", Case.Sensitive);
        script.ShouldContain("ALTER TABLE [Bills] ADD [SequenceNumber] int NULL", Case.Sensitive);
        script.ShouldContain("ALTER TABLE [Bills] ADD [PostedAtUtc] datetimeoffset(3) NULL", Case.Sensitive);
        script.ShouldContain("ALTER TABLE [Bills] ADD [PostedByUserId] uniqueidentifier NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Bills_NumberIffPosted] CHECK (([Status] IN ('Draft', 'PendingApproval', 'Voided') AND [Number] IS NULL AND [PostedAtUtc] IS NULL) OR ([Status] IN ('Posted', 'PartiallyPaid', 'Settled') AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Bills_StatusMatchesBalance] CHECK (([Status] <> 'Posted' OR ([BalanceDue] = [Total] AND [Total] > 0)) AND ([Status] <> 'PartiallyPaid' OR ([BalanceDue] > 0 AND [BalanceDue] < [Total])) AND ([Status] <> 'Settled' OR [BalanceDue] = 0))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Bills_NumberParts] CHECK (", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Bills_TenantId_Number] ON [Bills] ([TenantId], [Number]) WHERE [Number] IS NOT NULL", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Bills_TenantId_FiscalYear_SequenceNumber] ON [Bills] ([TenantId], [FiscalYear], [SequenceNumber]) WHERE [SequenceNumber] IS NOT NULL", Case.Sensitive);
    }

    [Fact]
    public void The_table_of_a_draft_and_the_columns_of_a_posted_bill_are_two_migrations()
    {
        // 0004_Bills creates the table without the number; 0009_BillPosting adds it. If the number is in the CREATE
        // TABLE, 0004 was scaffolded with BillPostingConfiguration.cs still in the project (docs/runbook.md, section 8).
        var script = _context.GetService<IMigrator>().GenerateScript();
        var createBills = script[script.IndexOf("CREATE TABLE [Bills]", StringComparison.Ordinal)..];
        createBills = createBills[..createBills.IndexOf(");", StringComparison.Ordinal)];

        createBills.ShouldNotContain("[Number]");
        createBills.ShouldNotContain("[PostedAtUtc]");
        createBills.ShouldNotContain("CK_Bills_NumberIffPosted");
        createBills.ShouldContain("CONSTRAINT [CK_Bills_Status] CHECK ([Status] IN ('Draft', 'Voided', 'Posted', 'PartiallyPaid', 'Settled', 'PendingApproval'))", Case.Sensitive);
    }

    [Fact]
    public void The_tenth_migration_is_0010_Approvals()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(10, $"The Approvals migration is missing. {HowToAdd.Replace("<name>", "0010_Approvals", StringComparison.Ordinal)}");
        migrations[9].ShouldEndWith("_0010_Approvals");
    }

    [Fact]
    public void The_migrations_create_the_approval_requests_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [ApprovalRequests]", customMessage: $"The migrations do not create the ApprovalRequests table. {HowToAdd}");
        script.ShouldContain("[DocumentType] varchar(30) NOT NULL", Case.Sensitive);
        script.ShouldContain("[Action] varchar(30) NOT NULL", Case.Sensitive);
        script.ShouldContain("[MakerUserId] uniqueidentifier NOT NULL", Case.Sensitive);
        script.ShouldContain("[MakerComment] nvarchar(500) NULL", Case.Sensitive);
        script.ShouldContain("[CheckerUserId] uniqueidentifier NULL", Case.Sensitive);
        script.ShouldContain("[CheckerComment] nvarchar(500) NULL", Case.Sensitive);
        script.ShouldContain("[RequestedAtUtc] datetimeoffset(3) NOT NULL", Case.Sensitive);
        script.ShouldContain("[DecidedAtUtc] datetimeoffset(3) NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ApprovalRequests_CheckerIsNotMaker] CHECK ([CheckerUserId] IS NULL OR [CheckerUserId] <> [MakerUserId])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ApprovalRequests_Decided] CHECK (([Status] = 'Pending' AND [CheckerUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([Status] <> 'Pending' AND [CheckerUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ApprovalRequests_RejectionHasComment] CHECK ([Status] <> 'Rejected' OR [CheckerComment] IS NOT NULL)", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ApprovalRequests_Status] CHECK ([Status] IN ('Pending', 'Approved', 'Rejected'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ApprovalRequests_Action] CHECK ([Action] IN ('Post', 'Reverse', 'ChangePrefix'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ApprovalRequests_DocumentType] CHECK ([DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'NumberSeries'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_ApprovalRequests_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_ApprovalRequests_TenantId_DocumentType_DocumentId] ON [ApprovalRequests] ([TenantId], [DocumentType], [DocumentId]) WHERE [Status] = 'Pending'", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_ApprovalRequests_TenantId_Status_RequestedAtUtc] ON [ApprovalRequests] ([TenantId], [Status], [RequestedAtUtc])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_Bills_TenantId_Status] ON [Bills] ([TenantId], [Status]) WHERE [Status] = 'PendingApproval'", Case.Sensitive);
    }

    [Fact]
    public void The_eleventh_migration_is_0011_Payments()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(11, $"The Payments migration is missing. {HowToAdd.Replace("<name>", "0011_Payments", StringComparison.Ordinal)}");
        migrations[10].ShouldEndWith("_0011_Payments");
    }

    [Fact]
    public void The_migrations_create_the_payments_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();
        var start = script.IndexOf("CREATE TABLE [Payments]", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, $"The migrations do not create the Payments table. {HowToAdd}");
        var createPayments = script[start..script.IndexOf(");", start, StringComparison.Ordinal)];

        createPayments.ShouldContain("[Number] varchar(16) NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[FiscalYear] char(7) NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[SequenceNumber] int NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[PayerId] uniqueidentifier NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[PaymentDate] date NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[Method] varchar(20) NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[Reference] nvarchar(100) NULL", Case.Sensitive);
        createPayments.ShouldContain("[Amount] decimal(18,2) NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[UnappliedAmount] decimal(18,2) NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[Status] varchar(20) NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[Notes] nvarchar(500) NULL", Case.Sensitive);
        createPayments.ShouldContain("[PostingId] uniqueidentifier NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("[RowVersion] rowversion NOT NULL", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [CK_Payments_SequenceNumber] CHECK ([SequenceNumber] >= 1)", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [CK_Payments_Amount] CHECK ([Amount] > 0)", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [CK_Payments_UnappliedAmount] CHECK ([UnappliedAmount] BETWEEN 0 AND [Amount])", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [CK_Payments_Reference] CHECK ([Method] = 'Cash' OR [Reference] IS NOT NULL)", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [CK_Payments_Method] CHECK ([Method] IN ('Cash', 'Card', 'UPI', 'BankTransfer', 'Cheque'))", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [CK_Payments_Status] CHECK ([Status] IN ('Posted'))", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [FK_Payments_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [FK_Payments_PayerId] FOREIGN KEY ([PayerId]) REFERENCES [Payers] ([Id])", Case.Sensitive);
        createPayments.ShouldContain("CONSTRAINT [FK_Payments_PostingId] FOREIGN KEY ([PostingId]) REFERENCES [LedgerPostings] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Payments_TenantId_Number] ON [Payments] ([TenantId], [Number])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Payments_TenantId_FiscalYear_SequenceNumber] ON [Payments] ([TenantId], [FiscalYear], [SequenceNumber])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_Payments_TenantId_PayerId_Status] ON [Payments] ([TenantId], [PayerId], [Status]) INCLUDE ([UnappliedAmount])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_Payments_TenantId_PaymentDate] ON [Payments] ([TenantId], [PaymentDate]) INCLUDE ([Method], [Amount], [Status])", Case.Sensitive);
    }

    [Fact]
    public void The_twelfth_migration_is_0012_PaymentAllocations()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(12, $"The PaymentAllocations migration is missing. {HowToAdd.Replace("<name>", "0012_PaymentAllocations", StringComparison.Ordinal)}");
        migrations[11].ShouldEndWith("_0012_PaymentAllocations");
    }

    [Fact]
    public void The_migrations_create_the_payment_allocations_table_as_documented_and_the_payments_table_without_it()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();
        var start = script.IndexOf("CREATE TABLE [PaymentAllocations]", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, $"The migrations do not create the PaymentAllocations table. {HowToAdd}");
        var createAllocations = script[start..script.IndexOf(");", start, StringComparison.Ordinal)];

        // The table of payments comes first, in a migration of its own (docs/runbook.md, section 8).
        script.IndexOf("CREATE TABLE [Payments]", StringComparison.Ordinal).ShouldBeLessThan(start);
        script.IndexOf("_0011_Payments", StringComparison.Ordinal).ShouldBeLessThan(start, "the table belongs to the twelfth migration, not to the eleventh");

        createAllocations.ShouldContain("[PaymentId] uniqueidentifier NOT NULL", Case.Sensitive);
        createAllocations.ShouldContain("[BillId] uniqueidentifier NOT NULL", Case.Sensitive);
        createAllocations.ShouldContain("[Amount] decimal(18,2) NOT NULL", Case.Sensitive);
        createAllocations.ShouldContain("[Status] varchar(20) NOT NULL", Case.Sensitive);
        createAllocations.ShouldContain("[AllocatedAtUtc] datetimeoffset(3) NOT NULL", Case.Sensitive);
        createAllocations.ShouldContain("[AllocatedByUserId] uniqueidentifier NULL", Case.Sensitive);
        createAllocations.ShouldContain("[PostingId] uniqueidentifier NOT NULL", Case.Sensitive);
        createAllocations.ShouldNotContain("RowVersion", Case.Sensitive, "the row version of the payment covers its allocations");
        createAllocations.ShouldContain("CONSTRAINT [CK_PaymentAllocations_Amount] CHECK ([Amount] > 0)", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [CK_PaymentAllocations_Status] CHECK ([Status] IN ('Active'))", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [FK_PaymentAllocations_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [FK_PaymentAllocations_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payments] ([Id])", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [FK_PaymentAllocations_BillId] FOREIGN KEY ([BillId]) REFERENCES [Bills] ([Id])", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [FK_PaymentAllocations_PostingId] FOREIGN KEY ([PostingId]) REFERENCES [LedgerPostings] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_PaymentAllocations_TenantId_PaymentId] ON [PaymentAllocations] ([TenantId], [PaymentId])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_PaymentAllocations_TenantId_BillId] ON [PaymentAllocations] ([TenantId], [BillId]) INCLUDE ([Amount], [Status])", Case.Sensitive);
    }

    [Fact]
    public void The_thirteenth_migration_is_0013_CreditNotes()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBeGreaterThanOrEqualTo(13, $"The CreditNotes migration is missing. {HowToAdd.Replace("<name>", "0013_CreditNotes", StringComparison.Ordinal)}");
        migrations[12].ShouldEndWith("_0013_CreditNotes");
    }

    [Fact]
    public void The_migrations_create_the_three_credit_note_tables_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();
        var createCreditNotes = CreateTable(script, "CreditNotes");
        var createLines = CreateTable(script, "CreditNoteLines");
        var createAllocations = CreateTable(script, "CreditNoteAllocations");

        // The three tables come after the allocations of payments, in a migration of their own (docs/runbook.md, section 8).
        script.IndexOf("_0012_PaymentAllocations", StringComparison.Ordinal).ShouldBeLessThan(script.IndexOf("CREATE TABLE [CreditNotes]", StringComparison.Ordinal));

        createCreditNotes.ShouldContain("[BillId] uniqueidentifier NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[PayerId] uniqueidentifier NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[PatientId] uniqueidentifier NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[CreditNoteDate] date NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[Reason] nvarchar(500) NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[Status] varchar(20) NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[Total] decimal(18,2) NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[AvailableAmount] decimal(18,2) NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[Number] varchar(16) NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("[RowVersion] rowversion NOT NULL", Case.Sensitive);
        createCreditNotes.ShouldContain("CONSTRAINT [CK_CreditNotes_Status] CHECK ([Status] IN ('Draft', 'PendingApproval', 'Posted', 'Voided'))", Case.Sensitive);
        createCreditNotes.ShouldContain("CONSTRAINT [CK_CreditNotes_AvailableAmount] CHECK ([AvailableAmount] BETWEEN 0 AND [Total])", Case.Sensitive);
        createCreditNotes.ShouldContain("CONSTRAINT [CK_CreditNotes_Total] CHECK ([Total] >= 0 AND [Total] = [SubTotal] - [DiscountTotal] + [TaxTotal])", Case.Sensitive);
        createCreditNotes.ShouldContain("CONSTRAINT [CK_CreditNotes_NumberIffPosted]", Case.Sensitive);
        createCreditNotes.ShouldContain("CONSTRAINT [FK_CreditNotes_BillId] FOREIGN KEY ([BillId]) REFERENCES [Bills] ([Id])", Case.Sensitive);
        createCreditNotes.ShouldContain("CONSTRAINT [FK_CreditNotes_PayerId] FOREIGN KEY ([PayerId]) REFERENCES [Payers] ([Id])", Case.Sensitive);
        createCreditNotes.ShouldContain("CONSTRAINT [FK_CreditNotes_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_CreditNotes_TenantId_BillId_Status] ON [CreditNotes] ([TenantId], [BillId], [Status]) INCLUDE ([Total])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_CreditNotes_TenantId_PayerId_Status] ON [CreditNotes] ([TenantId], [PayerId], [Status]) INCLUDE ([AvailableAmount])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_CreditNotes_TenantId_Number] ON [CreditNotes] ([TenantId], [Number]) WHERE [Number] IS NOT NULL", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_CreditNotes_TenantId_FiscalYear_SequenceNumber] ON [CreditNotes] ([TenantId], [FiscalYear], [SequenceNumber]) WHERE [SequenceNumber] IS NOT NULL", Case.Sensitive);

        createLines.ShouldContain("[CreditNoteId] uniqueidentifier NOT NULL", Case.Sensitive);
        createLines.ShouldContain("[BillLineId] uniqueidentifier NULL", Case.Sensitive);
        createLines.ShouldContain("[Quantity] decimal(9,3) NOT NULL", Case.Sensitive);
        createLines.ShouldContain("[UnitPrice] decimal(19,4) NOT NULL", Case.Sensitive);
        createLines.ShouldContain("[TaxRatePercent] decimal(5,2) NOT NULL", Case.Sensitive);
        createLines.ShouldNotContain("RowVersion", Case.Sensitive, "the row version of the credit note covers its lines");
        createLines.ShouldContain("CONSTRAINT [CK_CreditNoteLines_LineTotal] CHECK ([LineTotal] = [LineAmount] - [DiscountAmount] + [TaxAmount])", Case.Sensitive);
        createLines.ShouldContain("CONSTRAINT [FK_CreditNoteLines_CreditNoteId] FOREIGN KEY ([CreditNoteId]) REFERENCES [CreditNotes] ([Id])", Case.Sensitive);
        createLines.ShouldContain("CONSTRAINT [FK_CreditNoteLines_BillLineId] FOREIGN KEY ([BillLineId]) REFERENCES [BillLines] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_CreditNoteLines_TenantId_CreditNoteId_LineNo] ON [CreditNoteLines] ([TenantId], [CreditNoteId], [LineNo])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_CreditNoteLines_TenantId_BillLineId] ON [CreditNoteLines] ([TenantId], [BillLineId]) INCLUDE ([LineTotal])", Case.Sensitive);

        createAllocations.ShouldContain("[Amount] decimal(18,2) NOT NULL", Case.Sensitive);
        createAllocations.ShouldContain("[PostingId] uniqueidentifier NOT NULL", Case.Sensitive);
        createAllocations.ShouldNotContain("RowVersion", Case.Sensitive, "the row version of the credit note covers its applications");
        createAllocations.ShouldContain("CONSTRAINT [CK_CreditNoteAllocations_Amount] CHECK ([Amount] > 0)", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [CK_CreditNoteAllocations_Status] CHECK ([Status] IN ('Active'))", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [FK_CreditNoteAllocations_CreditNoteId] FOREIGN KEY ([CreditNoteId]) REFERENCES [CreditNotes] ([Id])", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [FK_CreditNoteAllocations_BillId] FOREIGN KEY ([BillId]) REFERENCES [Bills] ([Id])", Case.Sensitive);
        createAllocations.ShouldContain("CONSTRAINT [FK_CreditNoteAllocations_PostingId] FOREIGN KEY ([PostingId]) REFERENCES [LedgerPostings] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_CreditNoteAllocations_TenantId_CreditNoteId] ON [CreditNoteAllocations] ([TenantId], [CreditNoteId])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_CreditNoteAllocations_TenantId_BillId] ON [CreditNoteAllocations] ([TenantId], [BillId]) INCLUDE ([Amount], [Status])", Case.Sensitive);
    }

    // The text of one CREATE TABLE statement of the script of all migrations.
    private static string CreateTable(string script, string table)
    {
        var start = script.IndexOf($"CREATE TABLE [{table}]", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, $"The migrations do not create the {table} table. {HowToAdd}");

        return script[start..script.IndexOf(");", start, StringComparison.Ordinal)];
    }

    [Fact]
    public void The_migrations_create_the_bills_and_bill_lines_tables_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [Bills]", customMessage: $"The migrations do not create the Bills table. {HowToAdd}");
        script.ShouldContain("CREATE TABLE [BillLines]", customMessage: $"The migrations do not create the BillLines table. {HowToAdd}");
        script.ShouldContain("[Total] decimal(18,2) NOT NULL", Case.Sensitive);
        script.ShouldContain("[Quantity] decimal(9,3) NOT NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Bills_Status] CHECK ([Status] IN ('Draft', 'Voided', 'Posted', 'PartiallyPaid', 'Settled', 'PendingApproval'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Bills_Total] CHECK ([Total] >= 0 AND [Total] = [SubTotal] - [DiscountTotal] + [TaxTotal])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Bills_BalanceDue] CHECK ([BalanceDue] BETWEEN 0 AND [Total])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_BillLines_LineTotal] CHECK ([LineTotal] = [LineAmount] - [DiscountAmount] + [TaxAmount])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_Bills_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_Bills_PayerId] FOREIGN KEY ([PayerId]) REFERENCES [Payers] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_BillLines_BillId] FOREIGN KEY ([BillId]) REFERENCES [Bills] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_BillLines_ServiceItemId] FOREIGN KEY ([ServiceItemId]) REFERENCES [ServiceItems] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_BillLines_TenantId_BillId_LineNo] ON [BillLines] ([TenantId], [BillId], [LineNo])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_Bills_TenantId_PayerId_Status] ON [Bills] ([TenantId], [PayerId], [Status]) INCLUDE ([BalanceDue], [Total])", Case.Sensitive);
        script.ShouldNotContain("CASCADE", Case.Insensitive);
    }

    [Fact]
    public void The_migrations_create_the_service_items_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [ServiceItems]", customMessage: $"The migrations do not create the ServiceItems table. {HowToAdd}");
        script.ShouldContain("[Code] varchar(30) NOT NULL", Case.Sensitive);
        script.ShouldContain("[DefaultUnitPrice] decimal(19,4) NOT NULL", Case.Sensitive);
        script.ShouldContain("[TaxRatePercent] decimal(5,2) NOT NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ServiceItems_DefaultUnitPrice] CHECK ([DefaultUnitPrice] >= 0)", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ServiceItems_TaxRatePercent] CHECK ([TaxRatePercent] BETWEEN 0 AND 100)", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_ServiceItems_Status] CHECK ([Status] IN ('Active', 'Inactive'))", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_ServiceItems_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_ServiceItems_TenantId_Code] ON [ServiceItems] ([TenantId], [Code])", Case.Sensitive);
        script.ShouldContain("CREATE INDEX [IX_ServiceItems_TenantId_Name] ON [ServiceItems] ([TenantId], [Name])", Case.Sensitive);
    }

    [Fact]
    public void The_migrations_create_the_patients_and_payers_tables_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [Patients]", customMessage: $"The migrations do not create the Patients table. {HowToAdd}");
        script.ShouldContain("CREATE TABLE [Payers]", customMessage: $"The migrations do not create the Payers table. {HowToAdd}");
        script.ShouldContain("[Mrn] nvarchar(30) NOT NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_Patients_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_Payers_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [FK_Payers_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Patients_TenantId_Mrn] ON [Patients] ([TenantId], [Mrn])", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Payers_TenantId_PatientId] ON [Payers] ([TenantId], [PatientId]) WHERE [PatientId] IS NOT NULL", Case.Sensitive);
        script.ShouldNotContain("CASCADE", Case.Insensitive);
    }

    [Fact]
    public void The_model_has_no_change_that_is_missing_from_the_migrations()
    {
        _context.Database.HasPendingModelChanges().ShouldBeFalse(
            $"The model differs from the last migration: a migration is missing. {HowToAdd}");
    }

    [Fact]
    public void The_migrations_create_the_tenants_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [Tenants]", customMessage: $"The migrations do not create the Tenants table. {HowToAdd}");
        script.ShouldContain("[Code] varchar(20) NOT NULL", Case.Sensitive);
        script.ShouldContain("[PostingApprovalThreshold] decimal(18,2) NULL", Case.Sensitive);
        script.ShouldContain("[CreatedAtUtc] datetimeoffset(3) NOT NULL", Case.Sensitive);
        script.ShouldContain("[RowVersion] rowversion NOT NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Tenants_Status] CHECK ([Status] IN ('Active', 'Suspended'))", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Tenants_Code] ON [Tenants] ([Code])", Case.Sensitive);
    }
}
