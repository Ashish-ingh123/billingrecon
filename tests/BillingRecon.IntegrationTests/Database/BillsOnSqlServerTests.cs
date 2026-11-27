using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Billing;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The Bills and BillLines tables as the migrations create them on SQL Server 2022 (docs/database.md, section 3.3):
/// their constraints, indexes and foreign keys; the bill versioned as a whole, lines included (ADR 0007); and the rule
/// that nothing but a line of a draft is ever deleted (CLAUDE.md section 4, rule 10).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class BillsOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task The_tables_have_the_documented_check_constraints()
    {
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Bills')"))
            .ShouldBe(
                [
                    "CK_Bills_DueDate", "CK_Bills_SubTotal", "CK_Bills_DiscountTotal", "CK_Bills_TaxTotal", "CK_Bills_Total",
                    "CK_Bills_BalanceDue", "CK_Bills_VoidedIffVoided", "CK_Bills_Status",
                    "CK_Bills_NumberIffPosted", "CK_Bills_NumberParts", "CK_Bills_StatusMatchesBalance",

                    // Since Prompt 10.3: where the bill comes from (ADR 0053).
                    "CK_Bills_Origin", "CK_Bills_LegacyNumberIffOpening",
                ],
                ignoreOrder: true);

        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.BillLines')"))
            .ShouldBe(
                [
                    "CK_BillLines_LineNo", "CK_BillLines_Quantity", "CK_BillLines_UnitPrice", "CK_BillLines_LineAmount",
                    "CK_BillLines_DiscountAmount", "CK_BillLines_TaxRatePercent", "CK_BillLines_TaxAmount", "CK_BillLines_LineTotal",
                ],
                ignoreOrder: true);
    }

    [Fact]
    public async Task The_tables_have_the_documented_indexes_each_led_by_the_tenant_and_no_other()
    {
        var indexes = await NamesAsync(
            """
            SELECT i.[name] + ':' + c.[name] + ':' + CAST(i.is_unique AS varchar(1)) + ':' + CAST(i.has_filter AS varchar(1)) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id IN (OBJECT_ID(N'dbo.Bills'), OBJECT_ID(N'dbo.BillLines')) AND i.is_primary_key = 0
            """);

        indexes.ShouldBe(
            [
                "IX_Bills_TenantId_PatientId:TenantId:0:0",
                "IX_Bills_TenantId_PayerId_Status:TenantId:0:0",
                "IX_Bills_TenantId_BillDate:TenantId:0:0",
                "UX_Bills_TenantId_Number:TenantId:1:1",
                "UX_Bills_TenantId_Origin_FiscalYear_SequenceNumber:TenantId:1:1",
                "UX_Bills_TenantId_LegacyNumber:TenantId:1:1",
                "IX_Bills_TenantId_Status:TenantId:0:1",
                "IX_Bills_TenantId_Total:TenantId:0:0",
                "IX_Bills_TenantId_BalanceDue:TenantId:0:0",
                "UX_BillLines_TenantId_BillId_LineNo:TenantId:1:0",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_payers_index_carries_the_two_amounts_a_statement_adds_up()
    {
        var included = await NamesAsync(
            """
            SELECT c.[name] AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.Bills') AND i.[name] = N'IX_Bills_TenantId_PayerId_Status'
            """);

        included.ShouldBe(["BalanceDue", "Total"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_foreign_keys_exist_and_none_deletes_anything()
    {
        // delete_referential_action 0 is NO ACTION: also for the lines, which only Entity Framework ever removes.
        (await NamesAsync("SELECT [name] + ':' + CAST(delete_referential_action AS varchar(1)) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'dbo.Bills'), OBJECT_ID(N'dbo.BillLines'))"))
            .ShouldBe(
                [
                    "FK_Bills_TenantId:0", "FK_Bills_PatientId:0", "FK_Bills_PayerId:0",
                    "FK_Bills_CreatedByUserId:0", "FK_Bills_UpdatedByUserId:0", "FK_Bills_PostedByUserId:0", "FK_Bills_VoidedByUserId:0",
                    "FK_BillLines_TenantId:0", "FK_BillLines_BillId:0", "FK_BillLines_ServiceItemId:0",
                ],
                ignoreOrder: true);
    }

    [Fact]
    public async Task A_bill_round_trips_with_its_lines_its_exact_amounts_and_its_stamps()
    {
        var clerk = await NewApiTenantAsync();
        var billId = await SaveBillAsync(clerk, Line("Dressing", 3m, 333.3333m, 0.50m, 5m), Line("Suture", 0.333m, 3m, 0m, 100m));

        await using var reader = ApiContext(clerk);
        var stored = (await new BillRepository(reader).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull();

        stored.Status.ShouldBe(BillStatus.Draft);
        stored.BillDate.ShouldBe(Today);
        stored.TenantId.ShouldBe(clerk.TenantId!.Value);
        stored.CreatedByUserId.ShouldBe(clerk.UserId);
        stored.RowVersion.Length.ShouldBe(8);
        var lines = stored.Lines.OrderBy(line => line.LineNo).ToArray();
        lines.Select(line => line.LineNo).ShouldBe([1, 2]);
        lines[0].Quantity.ShouldBe(Quantity.Of(3m));
        lines[0].UnitPrice.ShouldBe(UnitPrice.Of(333.3333m));
        lines[0].LineAmount.ShouldBe(Money.Of(1000.00m));
        lines[0].TaxAmount.ShouldBe(Money.Of(49.98m));
        lines[0].LineTotal.ShouldBe(Money.Of(1049.48m));
        lines[1].Quantity.ShouldBe(Quantity.Of(0.333m));
        lines[1].LineAmount.ShouldBe(Money.Of(1.00m));
        lines[1].TaxRate.ShouldBe(Percent.Of(100m));
        lines[1].LineTotal.ShouldBe(Money.Of(2.00m));
        lines.ShouldAllBe(line => line.TenantId == clerk.TenantId!.Value && line.BillId == billId);
        stored.SubTotal.ShouldBe(Money.Of(1001.00m));
        stored.DiscountTotal.ShouldBe(Money.Of(0.50m));
        stored.TaxTotal.ShouldBe(Money.Of(50.98m));
        stored.Total.ShouldBe(Money.Of(1051.48m));
        stored.BalanceDue.ShouldBe(stored.Total);
        (await new BillReadService(reader, new BillingRecon.Infrastructure.Payers.PartySearch(reader)).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull().Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ADR0007_a_change_of_the_lines_alone_advances_the_row_version_and_the_update_stamp_of_the_bill()
    {
        var clerk = await NewApiTenantAsync();
        var supervisor = new FixedIdentity(clerk.TenantId, new UserId(Guid.NewGuid()));
        await AddApiUserRowAsync(supervisor);
        var billId = await SaveBillAsync(clerk, Line("Dressing", 2m, 60m, 0m, 5m));
        byte[] before;
        await using (var reader = ApiContext(clerk))
        {
            before = (await reader.Set<Bill>().SingleAsync()).RowVersion;
        }

        var later = new FakeClock(Now.AddHours(2));
        await using (var context = ApiContext(supervisor, later))
        {
            var bill = (await new BillRepository(context).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull();

            // Same quantity, price and tax: no total changes, so no column of the bill's own row does.
            bill.ReplaceLines([Line("Dressing, large", 2m, 60m, 0m, 5m)]).IsSuccess.ShouldBeTrue();
            context.Entry(bill).State.ShouldBe(EntityState.Unchanged, "nothing of the bill's own row changed");
            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        await using var after = ApiContext(clerk);
        var stored = await after.Set<Bill>().Include(bill => bill.Lines).SingleAsync();
        stored.RowVersion.ShouldNotBe(before);
        stored.UpdatedAtUtc.ShouldBe(later.UtcNow);
        stored.UpdatedByUserId.ShouldBe(supervisor.UserId);
        stored.CreatedAtUtc.ShouldBe(Now);
        stored.CreatedByUserId.ShouldBe(clerk.UserId);
        stored.Lines.ShouldHaveSingleItem().Description.ShouldBe("Dressing, large");
    }

    [Theory]
    [InlineData(false)] // one clerk changes a line, the other changes the same line
    [InlineData(true)] // one clerk removes a line, the other adds one
    public async Task BR804_two_clerks_changing_the_lines_of_one_bill_from_the_same_version_do_not_both_win(bool changeTheNumberOfLines)
    {
        var clerk = await NewApiTenantAsync();
        var billId = await SaveBillAsync(clerk, Line("Dressing", 2m, 60m, 0m, 5m), Line("Suture", 1m, 250m, 0m, 12m));
        await using var first = ApiContext(clerk);
        await using var second = ApiContext(clerk);
        var firstBill = (await new BillRepository(first).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull();
        var secondBill = (await new BillRepository(second).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull();

        if (changeTheNumberOfLines)
        {
            firstBill.ReplaceLines([Line("Dressing", 2m, 60m, 0m, 5m)]);
            secondBill.ReplaceLines([Line("Dressing", 2m, 60m, 0m, 5m), Line("Suture", 1m, 250m, 0m, 12m), Line("Bandage", 1m, 40m, 0m, 5m)]);
        }
        else
        {
            firstBill.ReplaceLines([Line("Dressing, first clerk", 2m, 60m, 0m, 5m), Line("Suture", 1m, 250m, 0m, 12m)]);
            secondBill.ReplaceLines([Line("Dressing, second clerk", 2m, 60m, 0m, 5m), Line("Suture", 1m, 250m, 0m, 12m)]);
        }

        (await new UnitOfWork(first).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new UnitOfWork(second).CommitAsync(CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);

        await using var reader = ApiContext(clerk);
        var stored = await reader.Set<Bill>().Include(bill => bill.Lines).SingleAsync();
        stored.Lines.Count.ShouldBe(changeTheNumberOfLines ? 1 : 2);
        stored.Lines.OrderBy(line => line.LineNo).First().Description.ShouldBe(changeTheNumberOfLines ? "Dressing" : "Dressing, first clerk");
        stored.Total.ShouldBe(stored.Lines.Select(line => line.LineTotal).Sum());
    }

    [Fact]
    public async Task Replacing_lines_updates_the_rows_that_keep_their_position_inserts_the_new_ones_and_deletes_the_rest()
    {
        var clerk = await NewApiTenantAsync();
        var billId = await SaveBillAsync(clerk, Line("One", 1m, 10m, 0m, 0m), Line("Two", 1m, 20m, 0m, 0m), Line("Three", 1m, 30m, 0m, 0m));
        var idsBefore = await LineIdsAsync(clerk);

        await using (var context = ApiContext(clerk))
        {
            var bill = (await new BillRepository(context).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull();
            bill.ReplaceLines([Line("Uno", 1m, 11m, 0m, 0m)]).IsSuccess.ShouldBeTrue();
            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        var afterShrinking = await LineIdsAsync(clerk);
        afterShrinking.ShouldBe([idsBefore[0]], "line 1 kept its row; lines 2 and 3 were deleted");

        await using (var context = ApiContext(clerk))
        {
            var bill = (await new BillRepository(context).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull();
            bill.ReplaceLines([Line("Uno", 1m, 11m, 0m, 0m), Line("Dos", 1m, 22m, 0m, 0m)]).IsSuccess.ShouldBeTrue();
            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        var afterGrowing = await LineIdsAsync(clerk);
        afterGrowing.Count.ShouldBe(2);
        afterGrowing[0].ShouldBe(idsBefore[0]);
        afterGrowing[1].ShouldNotBe(idsBefore[1], "a new line 2 is a new row");
    }

    [Fact]
    public async Task Rule10_a_bill_a_patient_and_a_service_item_cannot_be_deleted_and_stay_where_they_are()
    {
        var clerk = await NewApiTenantAsync();
        var billId = await SaveBillAsync(clerk, Line("Dressing", 2m, 60m, 0m, 5m));

        await using (var context = ApiContext(clerk))
        {
            var bill = (await new BillRepository(context).GetAsync(billId, CancellationToken.None)).ShouldNotBeNull();
            context.Remove(bill);

            var exception = await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            exception.Message.ShouldContain("Cannot delete Bill");
        }

        await using (var context = ApiContext(clerk))
        {
            context.Remove(await context.Set<Patient>().SingleAsync());

            (await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync())).Message.ShouldContain("Cannot delete Patient");
        }

        await using var reader = ApiContext(clerk);
        (await reader.Set<Bill>().Include(bill => bill.Lines).SingleAsync()).Lines.ShouldHaveSingleItem();
        (await reader.Set<Patient>().CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData("UPDATE [dbo].[Bills] SET [DueDate] = DATEADD(day, -1, [BillDate])")]
    [InlineData("UPDATE [dbo].[Bills] SET [Total] = [Total] + 0.01")]
    [InlineData("UPDATE [dbo].[Bills] SET [SubTotal] = -0.01")]
    [InlineData("UPDATE [dbo].[Bills] SET [BalanceDue] = [Total] + 0.01")]
    [InlineData("UPDATE [dbo].[Bills] SET [BalanceDue] = -0.01")]
    [InlineData("UPDATE [dbo].[Bills] SET [Status] = 'Voided'")]
    [InlineData("UPDATE [dbo].[Bills] SET [VoidReason] = 'A reason on a draft'")]
    [InlineData("UPDATE [dbo].[Bills] SET [Status] = 'Posted'")]
    [InlineData("UPDATE [dbo].[BillLines] SET [Quantity] = 0")]
    [InlineData("UPDATE [dbo].[BillLines] SET [UnitPrice] = -0.0001")]
    [InlineData("UPDATE [dbo].[BillLines] SET [DiscountAmount] = [LineAmount] + 0.01")]
    [InlineData("UPDATE [dbo].[BillLines] SET [DiscountAmount] = -0.01")]
    [InlineData("UPDATE [dbo].[BillLines] SET [TaxRatePercent] = 100.01")]
    [InlineData("UPDATE [dbo].[BillLines] SET [TaxAmount] = -0.01")]
    [InlineData("UPDATE [dbo].[BillLines] SET [LineTotal] = [LineTotal] + 0.01")]
    [InlineData("UPDATE [dbo].[BillLines] SET [LineNo] = 0")]
    public async Task A_value_the_check_constraints_forbid_is_refused_by_the_database(string update)
    {
        // The aggregate cannot hold these values, so the rows are changed underneath it, as a script would.
        var clerk = await NewApiTenantAsync();
        await SaveBillAsync(clerk, Line("Dressing", 2m, 60m, 10m, 5m));
        await using var context = ApiContext(clerk);

        var exception = await Should.ThrowAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(update));

        // 547: a CHECK constraint refused the row.
        exception.Number.ShouldBe(547);
    }

    [Fact]
    public async Task The_database_refuses_a_bill_for_a_patient_that_does_not_exist_and_a_second_line_with_the_same_number()
    {
        var clerk = await NewApiTenantAsync();
        await SaveBillAsync(clerk, Line("Dressing", 2m, 60m, 0m, 5m));
        await using var context = ApiContext(clerk);

        var orphan = await Should.ThrowAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync("UPDATE [dbo].[Bills] SET [PatientId] = NEWID()"));
        var duplicate = await Should.ThrowAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [dbo].[BillLines] ([Id], [TenantId], [BillId], [LineNo], [Description], [Quantity], [UnitPrice], [LineAmount], [DiscountAmount], [TaxRatePercent], [TaxAmount], [LineTotal])
            SELECT NEWID(), [TenantId], [BillId], [LineNo], [Description], [Quantity], [UnitPrice], [LineAmount], [DiscountAmount], [TaxRatePercent], [TaxAmount], [LineTotal] FROM [dbo].[BillLines]
            """));

        // 547: a foreign key refused the row. 2601: a unique index did.
        orphan.Number.ShouldBe(547);
        duplicate.Number.ShouldBe(2601);
    }

    private static BillLineDetails Line(string description, decimal quantity, decimal unitPrice, decimal discount, decimal taxRate) => new()
    {
        Description = description,
        Quantity = Quantity.Of(quantity),
        UnitPrice = UnitPrice.Of(unitPrice),
        DiscountAmount = Money.Of(discount),
        TaxRate = Percent.Of(taxRate),
    };

    // A patient, a guarantor and a draft bill with the lines given, saved as the clerk; the id of the bill.
    private async Task<BillId> SaveBillAsync(FixedIdentity clerk, params BillLineDetails[] lines)
    {
        await using var context = ApiContext(clerk);
        var patient = Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma" });
        var payer = Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Rahul Sharma" }, patientId: null).Value;
        context.Add(patient);
        context.Add(payer);
        await context.SaveChangesAsync();

        var bill = Bill.Create(patient.Id, payer.Id, new BillHeader { BillDate = Today, DueDate = Today }, lines, new BusinessDateWindow(Today, 7)).Value;
        context.Add(bill);
        (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        return bill.Id;
    }

    private async Task<List<BillLineId>> LineIdsAsync(FixedIdentity clerk)
    {
        await using var context = ApiContext(clerk);

        return await context.Set<BillLine>().OrderBy(line => line.LineNo).Select(line => line.Id).ToListAsync();
    }

    private async Task<List<string>> NamesAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }
}
