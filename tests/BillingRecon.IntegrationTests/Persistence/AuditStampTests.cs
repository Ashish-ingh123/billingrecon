using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The audit columns (ADR 0015), exercised on the change tracker without a database: what a save stamps on insert,
/// what it stamps on update, and what it never writes again.
/// </summary>
public sealed class AuditStampTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 3, 31, 18, 29, 59, TimeSpan.Zero);

    private readonly FixedIdentity _identity = FixedIdentity.Random();
    private readonly FakeClock _clock = new(CreatedAt);

    private TenantId CurrentTenant => _identity.TenantId!.Value;

    private UserId CurrentUser => _identity.UserId!.Value;

    [Fact]
    public void An_added_entity_gets_the_instant_and_the_user_in_both_pairs_of_columns()
    {
        using var context = Context();
        var widget = new Widget("new");
        context.Add(widget);

        Stamper().Apply(context);

        widget.CreatedAtUtc.ShouldBe(CreatedAt);
        widget.CreatedByUserId.ShouldBe(CurrentUser);
        widget.UpdatedAtUtc.ShouldBe(CreatedAt);
        widget.UpdatedByUserId.ShouldBe(CurrentUser);
    }

    [Fact]
    public void System_work_has_no_user_so_the_user_columns_stay_null()
    {
        using var context = Context();
        var widget = new Widget("seeded");
        context.Add(widget);

        new AuditStampInterceptor(SystemIdentity.Instance, _clock).Apply(context);

        widget.CreatedAtUtc.ShouldBe(CreatedAt);
        widget.CreatedByUserId.ShouldBeNull();
        widget.UpdatedAtUtc.ShouldBe(CreatedAt);
        widget.UpdatedByUserId.ShouldBeNull();
    }

    [Fact]
    public void A_modified_entity_gets_a_new_modification_stamp_and_keeps_its_creation_stamp()
    {
        using var context = Context();
        var creator = new UserId(Guid.NewGuid());
        var widget = Existing(context, new Widget("old"), creator);
        widget.Rename("new");
        _clock.Advance(TimeSpan.FromHours(2));

        Stamper().Apply(context);

        widget.UpdatedAtUtc.ShouldBe(CreatedAt.AddHours(2));
        widget.UpdatedByUserId.ShouldBe(CurrentUser);
        widget.CreatedAtUtc.ShouldBe(CreatedAt);
        widget.CreatedByUserId.ShouldBe(creator);

        var entry = context.Entry(widget);
        entry.Property(entity => entity.UpdatedAtUtc).IsModified.ShouldBeTrue();
        entry.Property(entity => entity.UpdatedByUserId).IsModified.ShouldBeTrue();
        entry.Property(entity => entity.CreatedAtUtc).IsModified.ShouldBeFalse();
        entry.Property(entity => entity.CreatedByUserId).IsModified.ShouldBeFalse();
    }

    [Fact]
    public void An_entity_attached_with_other_creation_values_does_not_get_them_marked_for_update()
    {
        using var context = Context();

        // A detached copy with made-up creation values, attached with Update: Entity Framework marks as modified
        // every column an UPDATE may write.
        var widget = new Widget("detached copy");
        var entry = context.Entry(widget);
        entry.Property(entity => entity.Id).CurrentValue = new WidgetId(Guid.NewGuid());
        entry.Property(entity => entity.TenantId).CurrentValue = CurrentTenant;
        entry.Property(entity => entity.CreatedAtUtc).CurrentValue = DateTimeOffset.MinValue;
        context.Update(widget);

        entry.State.ShouldBe(EntityState.Modified);
        entry.Property(entity => entity.Name).IsModified.ShouldBeTrue();
        entry.Property(entity => entity.CreatedAtUtc).IsModified.ShouldBeFalse();
        entry.Property(entity => entity.CreatedByUserId).IsModified.ShouldBeFalse();
        entry.Property(entity => entity.TenantId).IsModified.ShouldBeFalse();
    }

    [Theory]
    [InlineData(typeof(Widget), nameof(Widget.CreatedAtUtc), PropertySaveBehavior.Ignore)]
    [InlineData(typeof(Widget), nameof(Widget.CreatedByUserId), PropertySaveBehavior.Ignore)]
    [InlineData(typeof(LogLine), nameof(LogLine.CreatedAtUtc), PropertySaveBehavior.Ignore)]
    [InlineData(typeof(LogLine), nameof(LogLine.CreatedByUserId), PropertySaveBehavior.Ignore)]
    [InlineData(typeof(Widget), nameof(Widget.TenantId), PropertySaveBehavior.Throw)]
    [InlineData(typeof(LogLine), nameof(LogLine.TenantId), PropertySaveBehavior.Throw)]
    [InlineData(typeof(Widget), nameof(Widget.UpdatedAtUtc), PropertySaveBehavior.Save)]
    [InlineData(typeof(Widget), nameof(Widget.Name), PropertySaveBehavior.Save)]
    public void An_update_can_never_contain_the_creation_columns_or_the_tenant(Type entity, string property, PropertySaveBehavior afterInsert)
    {
        using var context = Context();

        // Ignore: the column is left out of every UPDATE. Throw: a save that would change it fails. Save: written as usual.
        context.Model.FindEntityType(entity)!.FindProperty(property)!.GetAfterSaveBehavior().ShouldBe(afterInsert);
    }

    [Fact]
    public void An_append_only_entity_has_a_creation_stamp_only()
    {
        using var context = Context();
        var line = new LogLine("appended");
        context.Add(line);

        Stamper().Apply(context);

        line.CreatedAtUtc.ShouldBe(CreatedAt);
        line.CreatedByUserId.ShouldBe(CurrentUser);
        context.Model.FindEntityType(typeof(LogLine))!.FindProperty(nameof(IModificationAudited.UpdatedAtUtc)).ShouldBeNull();
    }

    [Fact]
    public void A_modified_append_only_entity_is_not_stamped_at_all()
    {
        using var context = Context();
        var line = new LogLine("old");
        context.Entry(line).Property(entity => entity.CreatedAtUtc).CurrentValue = CreatedAt;
        context.TrackExisting(line, CurrentTenant);
        line.Rewrite("new");
        var clock = new CountingClock(CreatedAt.AddDays(1));

        new AuditStampInterceptor(_identity, clock).Apply(context);

        line.CreatedAtUtc.ShouldBe(CreatedAt);
        clock.Reads.ShouldBe(0);
    }

    [Fact]
    public void All_rows_of_one_save_carry_the_same_instant_which_is_read_once()
    {
        var clock = new CountingClock(CreatedAt);
        using var context = Context();
        var first = new Widget("first");
        var second = new Widget("second");
        var line = new LogLine("third");
        context.AddRange(first, second, line);

        new AuditStampInterceptor(_identity, clock).Apply(context);

        clock.Reads.ShouldBe(1);
        new[] { first.CreatedAtUtc, second.CreatedAtUtc, line.CreatedAtUtc, first.UpdatedAtUtc, second.UpdatedAtUtc }.ShouldAllBe(instant => instant == CreatedAt);
    }

    [Fact]
    public void A_save_without_audited_entities_reads_neither_the_clock_nor_the_user()
    {
        // This identity throws when it is asked for the user, so the test fails if the interceptor asks.
        var noIdentity = new ThrowingIdentity();
        var clock = new CountingClock(CreatedAt);
        using var context = TestContexts.Create(noIdentity, clock);
        context.Add(new Setting("colour"));

        Should.NotThrow(() => new AuditStampInterceptor(noIdentity, clock).Apply(context));

        clock.Reads.ShouldBe(0);
    }

    [Fact]
    public void A_deleted_or_unchanged_entity_is_not_stamped()
    {
        using var context = Context();
        var removed = Existing(context, new Widget("removed"), CurrentUser);
        var untouched = Existing(context, new Widget("untouched"), CurrentUser);
        context.Remove(removed);
        _clock.Advance(TimeSpan.FromDays(1));

        Stamper().Apply(context);

        removed.UpdatedAtUtc.ShouldBe(CreatedAt);
        untouched.UpdatedAtUtc.ShouldBe(CreatedAt);
        context.Entry(untouched).State.ShouldBe(EntityState.Unchanged);
    }

    [Fact]
    public void SaveChanges_runs_the_stamping()
    {
        // No interceptor is handed to the context here: it attaches the stamping to itself.
        using var context = Context();
        var widget = new Widget("new");
        context.Add(widget);

        // The connection string points at a closed port, so the save itself fails; the stamping has happened by then.
        Should.Throw<Exception>(() => context.SaveChanges());

        widget.CreatedAtUtc.ShouldBe(CreatedAt);
        widget.CreatedByUserId.ShouldBe(CurrentUser);
        widget.TenantId.ShouldBe(CurrentTenant);
    }

    [Theory]
    [InlineData(nameof(Widget.CreatedAtUtc), "datetimeoffset(3)", false)]
    [InlineData(nameof(Widget.UpdatedAtUtc), "datetimeoffset(3)", false)]
    [InlineData(nameof(Widget.CreatedByUserId), "uniqueidentifier", true)]
    [InlineData(nameof(Widget.UpdatedByUserId), "uniqueidentifier", true)]
    public void The_audit_columns_have_the_documented_types(string property, string columnType, bool nullable)
    {
        using var context = Context();

        var column = context.Model.FindEntityType(typeof(Widget))!.FindProperty(property)!;

        column.GetColumnType().ShouldBe(columnType);
        column.IsNullable.ShouldBe(nullable);
    }

    private TestDbContext Context() => TestContexts.Create(_identity, _clock);

    private AuditStampInterceptor Stamper() => new(_identity, _clock);

    // A row that was created earlier, at CreatedAt by the given user, and is now tracked as unchanged.
    private Widget Existing(TestDbContext context, Widget widget, UserId creator)
    {
        var entry = context.Entry(widget);
        entry.Property(entity => entity.CreatedAtUtc).CurrentValue = CreatedAt;
        entry.Property(entity => entity.CreatedByUserId).CurrentValue = creator;
        entry.Property(entity => entity.UpdatedAtUtc).CurrentValue = CreatedAt;
        entry.Property(entity => entity.UpdatedByUserId).CurrentValue = creator;

        return context.TrackExisting(widget, CurrentTenant);
    }

    private sealed class CountingClock(DateTimeOffset utcNow) : IClock
    {
        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                return utcNow;
            }
        }

        public DateOnly TodayIst => throw new NotSupportedException();

        public DateOnly ToIstDate(DateTimeOffset instant) => throw new NotSupportedException();
    }
}
