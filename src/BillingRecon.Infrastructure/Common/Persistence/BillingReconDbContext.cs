using System.Reflection;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// The one EF Core context of the application (modular monolith, one database: ADR 0003). Modules add their tables
/// through <c>IEntityTypeConfiguration</c> classes in their own folder and reach them with <c>Set&lt;T&gt;()</c> in
/// their repositories; the context itself names no module's entity.
/// <para>
/// Conventions applied to every entity type of the model, so that no configuration can forget them (ADR 0015):
/// typed ids are stored as <c>uniqueidentifier</c> and generated when the entity is added; instants are
/// <c>datetimeoffset(3)</c>; business dates are <c>date</c>; enums are stored by name with a CHECK constraint; a
/// <c>RowVersion</c> is a concurrency token; creation columns are never part of an UPDATE; a decimal or text column
/// without an explicit size fails the model; every tenant-scoped entity is filtered by the current tenant; and the
/// tenant row itself (the entity whose id is a <see cref="TenantId"/>) is filtered to the current tenant's own row.
/// The model has no entity inheritance and no owned types; the conventions would need extending for those.
/// </para>
/// <para>
/// The two save interceptors (tenant guard, audit columns) are attached by the context itself, in
/// <see cref="OnConfiguring"/>. A context cannot exist without them, however it was created: by dependency
/// injection, by the design-time factory, or by hand.
/// </para>
/// <para>
/// The class is not sealed so that tests can derive a context that adds probe entities and still runs all of this.
/// </para>
/// </summary>
public class BillingReconDbContext : DbContext
{
    /// <summary>
    /// The name of the tenant filter. The filter is a named one, so a module that needs a filter of its own on a
    /// tenant-scoped entity adds another named filter and both apply. An unnamed filter next to it fails the model.
    /// </summary>
    public const string TenantFilterName = "Tenant";

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(BillingReconDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo ApplyTenantRowFilterMethod =
        typeof(BillingReconDbContext).GetMethod(nameof(ApplyTenantRowFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    /// <summary>Creates the context for the tenant and the user of the current scope.</summary>
    public BillingReconDbContext(DbContextOptions<BillingReconDbContext> options, ITenantContext tenantContext, ICurrentUser currentUser, IClock clock)
        : this((DbContextOptions)options, tenantContext, currentUser, clock)
    {
    }

    /// <summary>For derived contexts, whose options are typed for the derived class.</summary>
    protected BillingReconDbContext(DbContextOptions options, ITenantContext tenantContext, ICurrentUser currentUser, IClock clock)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);

        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>The assemblies searched for typed ids (types that implement <see cref="ITypedId"/>).</summary>
    protected virtual IEnumerable<Assembly> TypedIdAssemblies => [typeof(Domain.AssemblyMarker).Assembly];

    // The tenant filters read these properties on the context that runs the query, every time a query runs. Nothing
    // about the tenant is captured when the model is built: the model is built once and shared by all contexts.
    private TenantId CurrentTenantId
        => _tenantContext.TenantId
            ?? throw new TenantIsolationException(
                "A query on a tenant-scoped entity ran in a scope that has no tenant. System work may touch only tables that are not tenant-scoped (ADR 0009).");

    // The tenant row itself is visible to its own tenant only; system work, which has no tenant, sees every tenant.
    // The filter checks this Guid for null through EF.Parameter, so the check reaches SQL as "@p IS NULL" on a
    // parameter, which Entity Framework resolves from the value when it compiles the query: for system work the
    // predicate disappears from the SQL, for a tenant it becomes Id = @tenant. Without EF.Parameter the whole
    // comparison is evaluated in memory into a bit parameter and stays in the SQL as "@p = 1 OR ...".
    private Guid? TenantRowScope => _tenantContext.TenantId?.Value;

    private TenantId TenantRowScopeOrEmpty => _tenantContext.TenantId ?? default;

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Every save of every instance passes through both. They are not registered anywhere else.
        optionsBuilder.AddInterceptors(
            new TenantWriteGuardInterceptor(_tenantContext),
            new AuditStampInterceptor(_currentUser, _clock));
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Typed ids are stored as the Guid they wrap.
        foreach (var idType in TypedIdAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(IsTypedId))
        {
            configurationBuilder.Properties(idType).HaveConversion(typeof(TypedIdConverter<>).MakeGenericType(idType));
        }

        // Money and its siblings are stored as decimals with the scale docs/database.md section 1 gives each of them.
        // A configuration never repeats the precision; the convention applies to nullable properties too (ADR 0004).
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(18, 2);
        configurationBuilder.Properties<UnitPrice>().HaveConversion<UnitPriceConverter>().HavePrecision(19, 4);
        configurationBuilder.Properties<Quantity>().HaveConversion<QuantityConverter>().HavePrecision(9, 3);
        configurationBuilder.Properties<Percent>().HaveConversion<PercentConverter>().HavePrecision(5, 2);

        // Instants are datetimeoffset(3), business dates are date, enums are stored by name in a varchar column whose
        // length each configuration sets (docs/database.md, section 1).
        configurationBuilder.Properties<DateTimeOffset>().HavePrecision(3);
        configurationBuilder.Properties<DateOnly>().HaveColumnType("date");
        configurationBuilder.Properties<Enum>().HaveConversion<string>().AreUnicode(false);

        // Deliberately no default for decimals and strings: a column whose size nobody chose fails the model.
        configurationBuilder.Conventions.Add(_ => new ExplicitColumnSizeConvention());
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingReconDbContext).Assembly);

        // Everything below is applied to every entity type in the model, including the ones a derived context added
        // before calling this method. An entity type that enters the model after this point gets none of it; the
        // test ConventionCoverageTests walks the production model and fails for such a type.
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => entityType.BaseType is null && !entityType.IsOwned() && !entityType.HasSharedClrType)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var clrType = entityType.ClrType;
            var entity = modelBuilder.Entity(clrType);

            if (FindGenericBase(clrType, typeof(Entity<>)) is { } entityBase)
            {
                // The id is generated in the application when the entity is added, not by the database.
                var idType = entityBase.GetGenericArguments()[0];
                entity.Property(nameof(Entity<TenantId>.Id)).HasValueGenerator(typeof(TypedIdValueGenerator<>).MakeGenericType(idType));

                if (idType == typeof(TenantId))
                {
                    // The tenant row itself: a tenant reads its own row and no other (ADR 0009, ADR 0015). The write
                    // side is in TenantWriteGuardInterceptor.
                    ApplyTenantRowFilterMethod.MakeGenericMethod(clrType)
                        .Invoke(this, BindingFlags.DoNotWrapExceptions, binder: null, [modelBuilder], culture: null);
                }
            }

            if (FindGenericBase(clrType, typeof(AggregateRoot<>)) is not null)
            {
                entity.Ignore(nameof(AggregateRoot<TenantId>.DomainEvents));
            }

            if (typeof(IHasRowVersion).IsAssignableFrom(clrType))
            {
                entity.Property(nameof(IHasRowVersion.RowVersion)).IsRowVersion();
            }

            if (typeof(ICreationAudited).IsAssignableFrom(clrType))
            {
                // Written by the INSERT and never again: an UPDATE leaves these columns out, whatever the entity in
                // memory holds (a detached copy, a mapping mistake).
                entity.Property(nameof(ICreationAudited.CreatedAtUtc)).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
                entity.Property(nameof(ICreationAudited.CreatedByUserId)).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            }

            if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                // Read side: every query is filtered by the current tenant. (Not wrapping exceptions keeps Entity
                // Framework's own message when it refuses the filter.)
                ApplyTenantFilterMethod.MakeGenericMethod(clrType)
                    .Invoke(this, BindingFlags.DoNotWrapExceptions, binder: null, [modelBuilder], culture: null);

                // Write side, in the statement itself: the tenant is part of the WHERE clause of every UPDATE and
                // DELETE, so a statement cannot touch another tenant's row even when the entity in memory claims to
                // belong to the current tenant, and the column can never be the target of an UPDATE (ADR 0015).
                var tenant = entity.Property(nameof(ITenantScoped.TenantId));
                tenant.IsConcurrencyToken();
                tenant.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            }

            AddEnumCheckConstraints(modelBuilder, entityType);
        }
    }

    private static bool IsTypedId(Type type) => type is { IsValueType: true, IsEnum: false } && typeof(ITypedId).IsAssignableFrom(type);

    private static Type? FindGenericBase(Type type, Type genericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == genericBase)
            {
                return current;
            }
        }

        return null;
    }

    // An enum is stored by name, and the column accepts only the names the enum has: CK_<Table>_<Column>. Adding a
    // member therefore shows up as a migration that replaces the constraint (docs/database.md, section 8).
    private static void AddEnumCheckConstraints(ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        var table = entityType.GetTableName();
        if (table is null)
        {
            return;
        }

        foreach (var property in entityType.GetDeclaredProperties().ToList())
        {
            var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (!enumType.IsEnum)
            {
                continue;
            }

            var column = property.GetColumnName();
            var names = string.Join(", ", Enum.GetNames(enumType).Select(name => $"'{name}'"));

            modelBuilder.Entity(entityType.ClrType)
                .ToTable(tableBuilder => tableBuilder.HasCheckConstraint($"CK_{table}_{column}", $"[{column}] IN ({names})"));
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
        => modelBuilder.Entity<TEntity>().HasQueryFilter(TenantFilterName, entity => entity.TenantId == CurrentTenantId);

    private void ApplyTenantRowFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : Entity<TenantId>
        => modelBuilder.Entity<TEntity>().HasQueryFilter(TenantFilterName, tenant => EF.Parameter(TenantRowScope) == null || tenant.Id == TenantRowScopeOrEmpty);
}
