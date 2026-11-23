using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The gapless number series on a real SQL Server (BR-801, BR-802; ADR 0008; docs/database.md section 4): numbers are
/// taken inside the transaction of the use case, one after the other however many postings run at once, and a posting
/// that fails gives its number back. No production document is numbered yet, so each test saves a
/// <see cref="NumberedDocument"/> probe in the same unit of work as the number it took, on the probe database, whose
/// tables are the production model plus the probes.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class NumberSeriesOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // The allocator is tested on its own: it takes numbers that no document carries.
    protected override IReadOnlyCollection<string> DriftLeftOnPurpose => [DriftIdentity.NumberSeriesGap];

    private const int Takers = 50;

    private static readonly FiscalYear Fy2026 = FiscalYear.Of(2026);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // docs/database.md section 4.3, for the probe table: any row is a gap or a duplicate.
    private const string GaplessVerification =
        """
        SELECT CONVERT(varchar(36), [TenantId]) + ':' + [DocumentType] + ':' + [FiscalYear] AS [Value]
        FROM   [dbo].[NumberedDocument]
        GROUP  BY [TenantId], [DocumentType], [FiscalYear]
        HAVING COUNT(*) <> MAX([SequenceNumber])
        """;

    [Fact]
    public async Task BR802_the_first_number_of_a_series_creates_its_row_with_the_tenants_default_prefix()
    {
        var tenant = await NewProbeTenantAsync();

        var first = await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);
        var second = await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026, new FakeClock(Now.AddMinutes(5)));

        first.Value.ShouldBe("BIL/26-27/000001");
        second.Value.ShouldBe("BIL/26-27/000002");
        second.Type.ShouldBe(DocumentType.Bill);
        second.FiscalYear.ShouldBe(Fy2026);

        await using var reader = ProbeContext(tenant);
        var series = await reader.Set<NumberSeries>().SingleAsync();
        series.TenantId.ShouldBe(tenant.TenantId!.Value);
        series.DocumentType.ShouldBe(DocumentType.Bill);
        series.FiscalYear.ShouldBe(Fy2026);
        series.Prefix.ShouldBe("BIL");
        series.NextNumber.ShouldBe(3);
        series.CreatedAtUtc.ShouldBe(Now);
        series.UpdatedAtUtc.ShouldBe(Now.AddMinutes(5));
        series.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData(DocumentType.Bill, "BIL/26-27/000001")]
    [InlineData(DocumentType.CreditNote, "CN/26-27/000001")]
    [InlineData(DocumentType.Payment, "PAY/26-27/000001")]
    [InlineData(DocumentType.Adjustment, "ADJ/26-27/000001")]
    [InlineData(DocumentType.Refund, "REF/26-27/000001")]
    public async Task BR801_every_document_type_has_its_own_series_and_its_own_prefix(DocumentType type, string expected)
    {
        var tenant = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);
        await TakeAndSaveAsync(tenant, DocumentType.Payment, Fy2026);

        // Whatever was taken before, the first number of another type is 1; of the same type it is the next one.
        var number = await TakeAndSaveAsync(tenant, type, Fy2026);

        number.Value.ShouldBe(type is DocumentType.Bill or DocumentType.Payment ? expected.Replace("000001", "000002", StringComparison.Ordinal) : expected);
    }

    [Fact]
    public async Task BR004_a_document_dated_31_march_takes_the_next_number_of_the_old_year_and_one_dated_1_april_starts_the_new()
    {
        var tenant = await NewProbeTenantAsync();
        var lastDay = new DateOnly(2027, 3, 31);
        var firstDay = new DateOnly(2027, 4, 1);
        await TakeAndSaveAsync(tenant, DocumentType.Bill, FiscalYear.FromDate(new DateOnly(2026, 10, 17)));
        await TakeAndSaveAsync(tenant, DocumentType.Bill, FiscalYear.FromDate(new DateOnly(2027, 1, 15)));

        var onTheLastDay = await TakeAndSaveAsync(tenant, DocumentType.Bill, FiscalYear.FromDate(lastDay));
        var onTheFirstDay = await TakeAndSaveAsync(tenant, DocumentType.Bill, FiscalYear.FromDate(firstDay));

        // Posted later, dated earlier: the fiscal year comes from the date of the document, not from the day it is
        // posted (ADR 0008), so the old series goes on after the new one has started.
        var postedLateForTheOldYear = await TakeAndSaveAsync(tenant, DocumentType.Bill, FiscalYear.FromDate(lastDay));
        var nextOfTheNewYear = await TakeAndSaveAsync(tenant, DocumentType.Bill, FiscalYear.FromDate(firstDay));

        onTheLastDay.Value.ShouldBe("BIL/26-27/000003");
        onTheFirstDay.Value.ShouldBe("BIL/27-28/000001");
        postedLateForTheOldYear.Value.ShouldBe("BIL/26-27/000004");
        nextOfTheNewYear.Value.ShouldBe("BIL/27-28/000002");
        (await GapsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BR801_fifty_parallel_takers_of_a_new_series_get_1_to_50_with_no_gap_and_no_duplicate(int round)
    {
        var tenant = await NewProbeTenantAsync();
        var fiscalYear = FiscalYear.Of(2026 + round);

        // The series does not exist yet: the fifty race to create its row as well as to take from it.
        var numbers = await TakeInParallelAsync(tenant, DocumentType.Bill, fiscalYear, Takers);

        numbers.Select(number => number.Sequence).Order().ShouldBe(Enumerable.Range(1, Takers));
        numbers.Select(number => number.Value).Distinct().Count().ShouldBe(Takers);
        numbers.ShouldAllBe(number => number.Prefix == "BIL" && number.FiscalYear == fiscalYear);
        (await GapsAsync()).ShouldBeEmpty();

        await using var reader = ProbeContext(tenant);
        (await reader.Set<NumberSeries>().SingleAsync()).NextNumber.ShouldBe(Takers + 1);
        (await reader.Set<NumberedDocument>().CountAsync()).ShouldBe(Takers);
    }

    [Fact]
    public async Task BR801_fifty_parallel_takers_of_an_existing_series_continue_it_without_a_gap_twice_over()
    {
        var tenant = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenant, DocumentType.Payment, Fy2026);

        var firstWave = await TakeInParallelAsync(tenant, DocumentType.Payment, Fy2026, Takers);
        var secondWave = await TakeInParallelAsync(tenant, DocumentType.Payment, Fy2026, Takers);

        firstWave.Select(number => number.Sequence).Order().ShouldBe(Enumerable.Range(2, Takers));
        secondWave.Select(number => number.Sequence).Order().ShouldBe(Enumerable.Range(2 + Takers, Takers));
        (await GapsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR801_parallel_takers_of_different_series_of_one_tenant_each_get_a_gapless_series()
    {
        var tenant = await NewProbeTenantAsync();

        var waves = await Task.WhenAll(
            TakeInParallelAsync(tenant, DocumentType.Bill, Fy2026, 20),
            TakeInParallelAsync(tenant, DocumentType.Payment, Fy2026, 20),
            TakeInParallelAsync(tenant, DocumentType.Bill, FiscalYear.Of(2027), 20));

        foreach (var wave in waves)
        {
            wave.Select(number => number.Sequence).Order().ShouldBe(Enumerable.Range(1, 20));
        }

        (await GapsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR801_a_number_taken_and_not_committed_is_handed_out_again_and_the_series_has_no_gap()
    {
        var tenant = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);

        // The request ends without a commit: the context is disposed, and the transaction with it.
        await using (var abandoned = ProbeContext(tenant))
        {
            var taken = await new NumberSeriesAllocator(abandoned, tenant, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
            taken.Sequence.ShouldBe(2);
            abandoned.Add(new NumberedDocument(taken, "never saved"));
        }

        var next = await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);

        next.Sequence.ShouldBe(2);
        (await GapsAsync()).ShouldBeEmpty();
        await using var reader = ProbeContext(tenant);
        (await reader.Set<NumberedDocument>().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task BR801_the_first_number_of_a_series_that_is_not_committed_leaves_no_series_row_behind()
    {
        var tenant = await NewProbeTenantAsync();

        await using (var abandoned = ProbeContext(tenant))
        {
            (await new NumberSeriesAllocator(abandoned, tenant, new FakeClock(Now)).NextAsync(DocumentType.Refund, Fy2026, CancellationToken.None)).Sequence.ShouldBe(1);
        }

        await using (var reader = ProbeContext(tenant))
        {
            (await reader.Set<NumberSeries>().CountAsync()).ShouldBe(0);
        }

        (await TakeAndSaveAsync(tenant, DocumentType.Refund, Fy2026)).Value.ShouldBe("REF/26-27/000001");
    }

    [Fact]
    public async Task US25_a_save_that_fails_after_the_number_was_taken_gives_the_number_back()
    {
        var tenant = await NewProbeTenantAsync();
        var first = await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);

        // A lost race: the unit of work answers duplicate_key, and rolls the number back with the document.
        await using (var context = ProbeContext(tenant))
        {
            var taken = await new NumberSeriesAllocator(context, tenant, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
            taken.Sequence.ShouldBe(2);
            context.Add(new NumberedDocument(first, "a second document with the first number"));

            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.DuplicateKeyCode);
            context.Database.CurrentTransaction.ShouldBeNull("the unit of work ended the transaction it rolled back");
        }

        // A fault: the save throws, and the number comes back all the same.
        await using (var context = ProbeContext(tenant))
        {
            var taken = await new NumberSeriesAllocator(context, tenant, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
            taken.Sequence.ShouldBe(2);
            context.Add(new NumberedDocument(taken, new string('t', 101)));

            await Should.ThrowAsync<DbUpdateException>(() => new UnitOfWork(context).CommitAsync(CancellationToken.None));
            context.Database.CurrentTransaction.ShouldBeNull();
        }

        var next = await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);

        next.Sequence.ShouldBe(2);
        (await GapsAsync()).ShouldBeEmpty();
        await using var reader = ProbeContext(tenant);
        (await reader.Set<NumberSeries>().SingleAsync()).NextNumber.ShouldBe(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BR801_a_second_taker_waits_for_the_first_and_then_gets_the_next_number_or_the_same_one(bool firstCommits)
    {
        var tenant = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);
        await using var first = ProbeContext(tenant);
        var held = await new NumberSeriesAllocator(first, tenant, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
        held.Sequence.ShouldBe(2);

        // The second posting asks while the first still holds the series.
        var second = Task.Run(() => TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026));
        var early = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(750)));
        early.ShouldNotBeSameAs(second, "the second taker must wait for the lock of the first");

        if (firstCommits)
        {
            first.Add(new NumberedDocument(held, "first"));
            (await new UnitOfWork(first).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }
        else
        {
            await first.Database.CurrentTransaction.ShouldNotBeNull().RollbackAsync();
        }

        var number = await second.WaitAsync(Patience);

        number.Sequence.ShouldBe(firstCommits ? 3 : 2);
        (await GapsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR002_two_tenants_have_their_own_series_and_an_existing_series_never_waits_for_another_tenants()
    {
        var tenantA = await NewProbeTenantAsync();
        var tenantB = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenantA, DocumentType.Bill, Fy2026);
        await TakeAndSaveAsync(tenantA, DocumentType.Bill, Fy2026);
        (await TakeAndSaveAsync(tenantB, DocumentType.Bill, Fy2026)).Value.ShouldBe("BIL/26-27/000001");

        // Tenant A holds its series, uncommitted. Tenant B's series exists, so B locks its own row and nothing else.
        await using var holding = ProbeContext(tenantA);
        (await new NumberSeriesAllocator(holding, tenantA, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None)).Sequence.ShouldBe(3);

        var secondOfB = await TakeAndSaveAsync(tenantB, DocumentType.Bill, Fy2026).WaitAsync(TimeSpan.FromSeconds(5));
        var thirdOfB = await TakeAndSaveAsync(tenantB, DocumentType.Bill, Fy2026).WaitAsync(TimeSpan.FromSeconds(5));

        secondOfB.Value.ShouldBe("BIL/26-27/000002");
        thirdOfB.Value.ShouldBe("BIL/26-27/000003");

        await using var asB = ProbeContext(tenantB);
        var series = await asB.Set<NumberSeries>().SingleAsync();
        series.TenantId.ShouldBe(tenantB.TenantId!.Value);
        series.NextNumber.ShouldBe(4);
    }

    [Fact]
    public async Task ADR0008_the_first_number_of_a_new_series_may_wait_for_a_neighbouring_series_and_always_gets_through()
    {
        // Creating a series locks the gap in the index where its row will go (HOLDLOCK). When the next row in the
        // index is another series whose transaction is open, even another tenant's, the first posting waits for that
        // transaction to end. Which series is the neighbour depends on the ids, so the test does not say whether the
        // waiting happens; it says that nobody waits for ever, that no deadlock follows, and that every tenant still
        // gets its own first number. Once a series exists it locks its own row only (the test above).
        var tenants = new List<FixedIdentity>();
        for (var index = 0; index < 8; index++)
        {
            tenants.Add(await NewProbeTenantAsync());
        }

        var holder = tenants[0];
        await TakeAndSaveAsync(holder, DocumentType.Bill, Fy2026);
        await using var holding = ProbeContext(holder);
        var held = await new NumberSeriesAllocator(holding, holder, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);

        // Seven other tenants post their first bill while the holder's transaction is open.
        var firsts = tenants.Skip(1).Select(tenant => Task.Run(() => TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026))).ToArray();
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        holding.Add(new NumberedDocument(held, "held"));
        (await new UnitOfWork(holding).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var numbers = await Task.WhenAll(firsts).WaitAsync(Patience);

        numbers.ShouldAllBe(number => number.Value == "BIL/26-27/000001");
        held.Sequence.ShouldBe(2);
        (await GapsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR802_a_series_keeps_its_prefix_when_the_tenants_default_changes_and_a_new_series_takes_the_new_one()
    {
        var tenant = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);
        await using (var system = ProbeContext(SystemIdentity.Instance))
        {
            (await system.Database.ExecuteSqlAsync($"UPDATE [dbo].[Tenants] SET [BillPrefix] = 'INV' WHERE [Id] = {tenant.TenantId!.Value.Value}")).ShouldBe(1);
        }

        var sameSeries = await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);
        var newSeries = await TakeAndSaveAsync(tenant, DocumentType.Bill, FiscalYear.Of(2027));

        sameSeries.Value.ShouldBe("BIL/26-27/000002");
        newSeries.Value.ShouldBe("INV/27-28/000001");
    }

    [Fact]
    public async Task Rule46_a_series_whose_next_number_no_longer_fits_sixteen_characters_fails_and_does_not_advance()
    {
        var tenant = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);
        await TakeAndSaveAsync(tenant, DocumentType.CreditNote, Fy2026);
        await using (var system = ProbeContext(SystemIdentity.Instance))
        {
            (await system.Database.ExecuteSqlAsync($"UPDATE [dbo].[NumberSeries] SET [NextNumber] = 1000000 WHERE [TenantId] = {tenant.TenantId!.Value.Value}")).ShouldBe(2);
        }

        await using (var context = ProbeContext(tenant))
        {
            var exception = await Should.ThrowAsync<InvalidOperationException>(
                () => new NumberSeriesAllocator(context, tenant, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None));
            exception.Message.ShouldContain("exhausted");
        }

        // The two-character prefix still has room: sixteen characters exactly.
        (await TakeAndSaveAsync(tenant, DocumentType.CreditNote, Fy2026)).Value.ShouldBe("CN/26-27/1000000");

        await using var reader = ProbeContext(tenant);
        var bills = await reader.Set<NumberSeries>().SingleAsync(series => series.DocumentType == DocumentType.Bill);
        bills.NextNumber.ShouldBe(1_000_000, "the failed take was rolled back with its transaction");
    }

    [Fact]
    public async Task A_scope_without_a_tenant_and_a_tenant_without_a_row_get_no_number_and_create_no_series()
    {
        var ghost = FixedIdentity.Random();

        await using (var system = ProbeContext(SystemIdentity.Instance))
        {
            await Should.ThrowAsync<InvalidOperationException>(
                () => new NumberSeriesAllocator(system, SystemIdentity.Instance, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None));
            system.Database.CurrentTransaction.ShouldBeNull("nothing was opened for a scope that has no tenant");
        }

        await using (var context = ProbeContext(ghost))
        {
            var exception = await Should.ThrowAsync<InvalidOperationException>(
                () => new NumberSeriesAllocator(context, ghost, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None));
            exception.Message.ShouldContain("has no row");
        }

        await using var reader = ProbeContext(ghost);
        (await reader.Set<NumberSeries>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task An_undefined_document_type_and_the_default_fiscal_year_are_bugs_and_throw_before_anything_is_opened()
    {
        var tenant = await NewProbeTenantAsync();
        await using var context = ProbeContext(tenant);
        var series = new NumberSeriesAllocator(context, tenant, new FakeClock(Now));

        (await Should.ThrowAsync<ArgumentException>(() => series.NextAsync((DocumentType)42, Fy2026, CancellationToken.None))).ParamName.ShouldBe("type");
        (await Should.ThrowAsync<ArgumentException>(() => series.NextAsync(DocumentType.Bill, default, CancellationToken.None))).ParamName.ShouldBe("fiscalYear");
        context.Database.CurrentTransaction.ShouldBeNull();
    }

    [Fact]
    public async Task ADR0033_a_held_series_that_does_not_exist_yet_is_created_only_after_the_holder_ends_with_the_default_of_that_moment()
    {
        var tenant = await NewProbeTenantAsync();
        await using var holding = ProbeContext(tenant);
        await new NumberSeriesAllocator(holding, tenant, new FakeClock(Now)).HoldAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
        holding.Database.CurrentTransaction.ShouldNotBeNull("the hold opens the transaction of the use case");

        // The first bill of the year is asked for while the series is held: it must not be created yet.
        var first = Task.Run(() => TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026));
        var early = await Task.WhenAny(first, Task.Delay(TimeSpan.FromMilliseconds(750)));
        early.ShouldNotBeSameAs(first, "the first number must wait for the hold");

        // What an approved prefix change writes in its transaction: the tenant's new default.
        (await holding.Database.ExecuteSqlAsync($"UPDATE [dbo].[Tenants] SET [BillPrefix] = 'INV' WHERE [Id] = {tenant.TenantId!.Value.Value}")).ShouldBe(1);
        await holding.Database.CurrentTransaction!.CommitAsync();

        (await first.WaitAsync(Patience)).Value.ShouldBe("INV/26-27/000001");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADR0033_holding_an_existing_series_makes_a_taker_wait_and_takes_no_number_itself(bool holderCommits)
    {
        var tenant = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026);
        await using var holding = ProbeContext(tenant);
        await new NumberSeriesAllocator(holding, tenant, new FakeClock(Now)).HoldAsync(DocumentType.Bill, Fy2026, CancellationToken.None);

        var second = Task.Run(() => TakeAndSaveAsync(tenant, DocumentType.Bill, Fy2026));
        var early = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(750)));
        early.ShouldNotBeSameAs(second, "the taker must wait for the hold");

        if (holderCommits)
        {
            await holding.Database.CurrentTransaction!.CommitAsync();
        }
        else
        {
            await holding.Database.CurrentTransaction!.RollbackAsync();
        }

        (await second.WaitAsync(Patience)).Value.ShouldBe("BIL/26-27/000002");
        await using var reader = ProbeContext(tenant);
        (await reader.Set<NumberSeries>().SingleAsync()).NextNumber.ShouldBe(3);
        (await GapsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ADR0033_a_hold_never_waits_for_another_tenants_existing_series_and_without_a_tenant_or_with_bad_arguments_opens_nothing()
    {
        var tenantA = await NewProbeTenantAsync();
        var tenantB = await NewProbeTenantAsync();
        await TakeAndSaveAsync(tenantA, DocumentType.Bill, Fy2026);
        await TakeAndSaveAsync(tenantB, DocumentType.Bill, Fy2026);
        await using var holdingA = ProbeContext(tenantA);
        await new NumberSeriesAllocator(holdingA, tenantA, new FakeClock(Now)).HoldAsync(DocumentType.Bill, Fy2026, CancellationToken.None);

        // B's series exists: B locks its own row and nothing of A's.
        (await TakeAndSaveAsync(tenantB, DocumentType.Bill, Fy2026).WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldBe("BIL/26-27/000002");

        await using var system = ProbeContext(SystemIdentity.Instance);
        await Should.ThrowAsync<InvalidOperationException>(
            () => new NumberSeriesAllocator(system, SystemIdentity.Instance, new FakeClock(Now)).HoldAsync(DocumentType.Bill, Fy2026, CancellationToken.None));
        system.Database.CurrentTransaction.ShouldBeNull("nothing was opened for a scope that has no tenant");

        await using var context = ProbeContext(tenantB);
        var series = new NumberSeriesAllocator(context, tenantB, new FakeClock(Now));
        (await Should.ThrowAsync<ArgumentException>(() => series.HoldAsync((DocumentType)42, Fy2026, CancellationToken.None))).ParamName.ShouldBe("type");
        (await Should.ThrowAsync<ArgumentException>(() => series.HoldAsync(DocumentType.Bill, default, CancellationToken.None))).ParamName.ShouldBe("fiscalYear");
        context.Database.CurrentTransaction.ShouldBeNull();
    }

    [Fact]
    public async Task One_context_can_post_twice_each_in_its_own_transaction()
    {
        var tenant = await NewProbeTenantAsync();
        await using var context = ProbeContext(tenant);
        var series = new NumberSeriesAllocator(context, tenant, new FakeClock(Now));
        var unitOfWork = new UnitOfWork(context);

        var first = await series.NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
        context.Database.CurrentTransaction.ShouldNotBeNull();
        context.Add(new NumberedDocument(first, "first"));
        (await unitOfWork.CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        context.Database.CurrentTransaction.ShouldBeNull();

        var second = await series.NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
        context.Add(new NumberedDocument(second, "second"));
        (await unitOfWork.CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        (first.Sequence, second.Sequence).ShouldBe((1, 2));
        (await GapsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_gapless_verification_query_reports_a_gap_and_a_series_that_does_not_start_at_one()
    {
        // The query of section 4.3 has to find what it is there to find: rows written past the series, as a script
        // or a defect would.
        var tenant = await NewProbeTenantAsync();
        var fy2027 = FiscalYear.Of(2027);
        await using (var context = ProbeContext(tenant))
        {
            context.Add(new NumberedDocument(DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 1), "one"));
            context.Add(new NumberedDocument(DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 3), "three: two is missing"));
            context.Add(new NumberedDocument(DocumentNumber.Create(DocumentType.Payment, fy2027, "PAY", 2), "two: one is missing"));
            context.Add(new NumberedDocument(DocumentNumber.Create(DocumentType.Refund, Fy2026, "REF", 1), "a sound series"));
            await context.SaveChangesAsync();
        }

        // SQL Server writes a uniqueidentifier in capitals.
        var tenantId = tenant.TenantId!.Value.Value.ToString().ToUpperInvariant();
        (await GapsAsync()).ShouldBe([$"{tenantId}:Bill:2026-27", $"{tenantId}:Payment:2027-28"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_migrated_table_has_the_documented_constraints_and_the_one_index_the_lock_relies_on()
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        var checks = await context.Database
            .SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.NumberSeries')")
            .ToListAsync();
        var indexes = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT i.[name] + ':' + CAST(i.is_unique AS varchar(1)) + ':' + STRING_AGG(c.[name], ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS [Value]
                FROM sys.indexes AS i
                JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID(N'dbo.NumberSeries') AND i.is_primary_key = 0
                GROUP BY i.[name], i.is_unique
                """)
            .ToListAsync();
        var foreignKeys = await context.Database
            .SqlQueryRaw<string>("SELECT [name] + ':' + CAST(delete_referential_action AS varchar(1)) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.NumberSeries')")
            .ToListAsync();

        checks.ShouldBe(["CK_NumberSeries_DocumentType", "CK_NumberSeries_Prefix", "CK_NumberSeries_NextNumber"], ignoreOrder: true);
        indexes.ShouldBe(["UX_NumberSeries_TenantId_DocumentType_FiscalYear:1:TenantId,DocumentType,FiscalYear"]);
        foreignKeys.ShouldBe(["FK_NumberSeries_TenantId:0"]);
    }

    [Theory]
    [InlineData("UPDATE [dbo].[NumberSeries] SET [NextNumber] = 0")]
    [InlineData("UPDATE [dbo].[NumberSeries] SET [Prefix] = 'bil'")]
    [InlineData("UPDATE [dbo].[NumberSeries] SET [Prefix] = 'B-L'")]
    [InlineData("UPDATE [dbo].[NumberSeries] SET [Prefix] = ''")]
    [InlineData("UPDATE [dbo].[NumberSeries] SET [DocumentType] = 'Invoice'")]
    public async Task A_value_the_check_constraints_forbid_is_refused_by_the_migrated_database(string update)
    {
        var tenant = await NewApiTenantAsync();
        await using (var context = ApiContext(tenant))
        {
            await new NumberSeriesAllocator(context, tenant, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        await using var writer = ApiContext(tenant);
        var exception = await Should.ThrowAsync<SqlException>(() => writer.Database.ExecuteSqlRawAsync(update));

        // 547: a CHECK constraint refused the row.
        exception.Number.ShouldBe(547);
    }

    [Fact]
    public async Task The_migrated_database_refuses_a_second_series_for_the_same_tenant_type_and_year()
    {
        var tenant = await NewApiTenantAsync();
        await using (var context = ApiContext(tenant))
        {
            await new NumberSeriesAllocator(context, tenant, new FakeClock(Now)).NextAsync(DocumentType.Bill, Fy2026, CancellationToken.None);
            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        await using var writer = ApiContext(tenant);
        var exception = await Should.ThrowAsync<SqlException>(() => writer.Database.ExecuteSqlRawAsync(
            """
            INSERT [dbo].[NumberSeries] ([Id], [TenantId], [DocumentType], [FiscalYear], [Prefix], [NextNumber], [CreatedAtUtc], [UpdatedAtUtc])
            SELECT NEWID(), [TenantId], [DocumentType], [FiscalYear], [Prefix], 1, [CreatedAtUtc], [UpdatedAtUtc] FROM [dbo].[NumberSeries]
            """));

        // 2601: a unique index refused the row.
        exception.Number.ShouldBe(2601);
    }

    // A tenant with its row in the probe database, where the probe documents live.
    private async Task<FixedIdentity> NewProbeTenantAsync()
    {
        var identity = FixedIdentity.Random();
        await AddProbeTenantRowsAsync(identity);

        return identity;
    }

    // One posting: its own context, a number, a document that carries it, one commit.
    private async Task<DocumentNumber> TakeAndSaveAsync(FixedIdentity tenant, DocumentType type, FiscalYear fiscalYear, FakeClock? clock = null)
    {
        clock ??= new FakeClock(Now);
        await using var context = ProbeContext(tenant, clock);

        var number = await new NumberSeriesAllocator(context, tenant, clock).NextAsync(type, fiscalYear, CancellationToken.None);
        context.Add(new NumberedDocument(number, "posted"));
        (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        return number;
    }

    // As many postings at once as the thread pool and the connection pool allow, started together.
    private async Task<DocumentNumber[]> TakeInParallelAsync(FixedIdentity tenant, DocumentType type, FiscalYear fiscalYear, int takers)
    {
        using var start = new SemaphoreSlim(0, takers);
        var postings = Enumerable.Range(0, takers)
            .Select(_ => Task.Run(async () =>
            {
                await start.WaitAsync();

                return await TakeAndSaveAsync(tenant, type, fiscalYear);
            }))
            .ToArray();

        start.Release(takers);

        return await Task.WhenAll(postings).WaitAsync(Patience);
    }

    private async Task<List<string>> GapsAsync()
    {
        await using var context = ProbeContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<string>(GaplessVerification).ToListAsync();
    }
}
