using System.Security.Cryptography;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Audit;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The operator's work on a hospital (ADR 0052), run from the server by the command line of the API, never by a
/// request. Each operation is one commit of a context made for the one tenant it acts on, with no user
/// (<see cref="OperatorIdentity"/>), through the unit of work, so its domain events are audited in the same
/// transaction with a correlation id of its own. Finding a tenant by its code is the one read across tenants, made with
/// <see cref="SystemIdentity"/>, which sees every tenant row and nothing tenant-scoped.
/// </summary>
public sealed class TenantOperations(
    DbContextOptions<BillingReconDbContext> options,
    IPasswordHasher passwordHasher,
    IClock clock) : ITenantOperations
{
    /// <summary>How many characters a one-time password has.</summary>
    public const int OneTimePasswordLength = 20;

    // No characters that read alike (0 and O, 1, l and I), so the password survives being read out or copied by hand.
    private static readonly string[] Placeholders = ["Z", "Y", "X", "W", "V"];

    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>The correlation id of the last operation, which its audit records carry; the command shows it.</summary>
    public Guid CorrelationId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public async Task<Result<OnboardedTenant>> OnboardAsync(OnboardTenant command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var fieldErrors = new Dictionary<string, string[]>();
        var tenant = Tenant.Onboard(command.Code, command.Name, now);
        if (tenant.IsFailure)
        {
            foreach (var (field, messages) in tenant.Error.FieldErrors)
            {
                fieldErrors[field] = messages;
            }
        }

        Check(fieldErrors, command);
        if (fieldErrors.Count > 0)
        {
            return IdentityErrors.TenantInvalid(fieldErrors);
        }

        if (await FindIdAsync(command.Code, ct) is not null)
        {
            return IdentityErrors.TenantCodeTaken();
        }

        var tenantId = new TenantId(Guid.NewGuid());
        await using var context = ContextFor(tenantId, out var unitOfWork);

        context.Add(tenant.Value);
        context.Entry(tenant.Value).Property(candidate => candidate.Id).CurrentValue = tenantId;
        tenant.Value.ChangeSettings(
            command.PostingApprovalThreshold is { } threshold ? Money.Of(threshold) : null,
            command.MaxBackdateDays,
            command.DefaultDueDays);
        ApplyPrefixes(tenant.Value, command.Prefixes);

        var password = RandomNumberGenerator.GetString(PasswordAlphabet, OneTimePasswordLength);
        var admin = User.Create(
            command.AdminEmail.Trim(),
            command.AdminDisplayName.Trim(),
            passwordHasher.Hash(password),
            [Role.TenantAdmin],
            now,
            grantedBy: null,
            passwordChangeDue: true);
        context.Add(admin);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // A tenant with this code was made between the check and the save.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? IdentityErrors.TenantCodeTaken() : committed.Error;
        }

        return new OnboardedTenant(tenantId, admin.Id, password);
    }

    /// <inheritdoc />
    public Task<Result> SuspendAsync(string code, string reason, CancellationToken ct) =>
        ChangeStatusAsync(code, reason, suspend: true, ct);

    /// <inheritdoc />
    public Task<Result> ResumeAsync(string code, string reason, CancellationToken ct) =>
        ChangeStatusAsync(code, reason, suspend: false, ct);

    private async Task<Result> ChangeStatusAsync(string code, string reason, bool suspend, CancellationToken ct)
    {
        if (reason is null || reason.Trim().Length is 0 or > Tenant.ReasonMaxLength)
        {
            return IdentityErrors.TenantInvalid(new Dictionary<string, string[]>
            {
                ["reason"] = [$"Give a reason of 1 to {Tenant.ReasonMaxLength} characters."],
            });
        }

        if (await FindIdAsync(code, ct) is not { } tenantId)
        {
            return IdentityErrors.TenantNotFound();
        }

        await using var context = ContextFor(tenantId, out var unitOfWork);
        var tenant = await context.Set<Tenant>().SingleAsync(candidate => candidate.Id == tenantId, ct);
        var changed = suspend ? tenant.Suspend(reason) : tenant.Resume(reason);
        if (!changed)
        {
            return IdentityErrors.TenantStatusUnchanged(tenant.Status);
        }

        return await unitOfWork.CommitAsync(ct);
    }

    private async Task<TenantId?> FindIdAsync(string code, CancellationToken ct)
    {
        await using var system = new BillingReconDbContext(options, SystemIdentity.Instance, SystemIdentity.Instance, clock);

        return await system.Set<Tenant>()
            .AsNoTracking()
            .Where(tenant => tenant.Code == code)
            .Select(tenant => (TenantId?)tenant.Id)
            .SingleOrDefaultAsync(ct);
    }

    private BillingReconDbContext ContextFor(TenantId tenantId, out UnitOfWork unitOfWork)
    {
        var identity = new OperatorIdentity(tenantId);
        var context = new BillingReconDbContext(options, identity, identity, clock);
        var audit = new AuditSubscriber(new AuditWriter(context), identity, new OperatorCorrelation(CorrelationId));
        unitOfWork = new UnitOfWork(context, subscribers: [audit]);

        return context;
    }

    private static void Check(Dictionary<string, string[]> fieldErrors, OnboardTenant command)
    {
        var email = command.AdminEmail?.Trim() ?? string.Empty;
        if (email.Length is 0 or > User.EmailMaxLength || !email.Contains('@', StringComparison.Ordinal))
        {
            fieldErrors["adminEmail"] = [$"Give the administrator's e-mail address, at most {User.EmailMaxLength} characters."];
        }

        if ((command.AdminDisplayName?.Trim().Length ?? 0) is 0 or > User.DisplayNameMaxLength)
        {
            fieldErrors["adminName"] = [$"Give the administrator's name, 1 to {User.DisplayNameMaxLength} characters."];
        }

        if (command.PostingApprovalThreshold is { } threshold && (threshold <= 0 || !Money.TryOf(threshold, out _)))
        {
            fieldErrors["threshold"] = ["The approval threshold is an amount above zero in rupees and paise, or none."];
        }

        if (command.MaxBackdateDays is < 0 or > Tenant.MaxDays)
        {
            fieldErrors["backdateDays"] = [$"The back-date window is 0 to {Tenant.MaxDays} days."];
        }

        if (command.DefaultDueDays is < 0 or > Tenant.MaxDays)
        {
            fieldErrors["dueDays"] = [$"The default due days are 0 to {Tenant.MaxDays}."];
        }

        var prefixes = command.Prefixes ?? new Dictionary<DocumentType, string>();
        if (prefixes.Any(prefix => !DocumentNumber.IsPrefix(prefix.Value)))
        {
            fieldErrors["prefixes"] = [$"A prefix has 1 to {DocumentNumber.PrefixMaxLength} characters, each a capital letter or a digit."];
        }
        else if (FinalPrefixes(prefixes).Values.Distinct(StringComparer.Ordinal).Count() != Enum.GetValues<DocumentType>().Length)
        {
            fieldErrors["prefixes"] = ["Each document type needs a prefix of its own."];
        }
    }

    private static Dictionary<DocumentType, string> FinalPrefixes(IReadOnlyDictionary<DocumentType, string> given)
    {
        var standard = Tenant.Create("standard", "standard", DateTimeOffset.UnixEpoch).Value.Prefixes;

        return standard.ToDictionary(pair => pair.Key, pair => given.TryGetValue(pair.Key, out var prefix) ? prefix : pair.Value);
    }

    // A prefix may be the standard prefix of another type that is being given a new one, so the changes are made in an
    // order in which no two types share a prefix at any step; the final set was checked to be distinct.
    private static void ApplyPrefixes(Tenant tenant, IReadOnlyDictionary<DocumentType, string> given)
    {
        var pending = given.Where(prefix => tenant.PrefixOf(prefix.Key) != prefix.Value).ToList();
        while (pending.Count > 0)
        {
            var next = pending.FindIndex(prefix => !tenant.Prefixes.Values.Contains(prefix.Value, StringComparer.Ordinal));
            if (next < 0)
            {
                // Two types swap prefixes: give one a placeholder first, which the loop then replaces.
                var first = pending[0];
                var placeholder = Placeholders.First(candidate => !tenant.Prefixes.Values.Contains(candidate, StringComparer.Ordinal) && !given.Values.Contains(candidate, StringComparer.Ordinal));
                tenant.ChangePrefix(first.Key, placeholder);
                continue;
            }

            tenant.ChangePrefix(pending[next].Key, pending[next].Value);
            pending.RemoveAt(next);
        }
    }

    private sealed class OperatorCorrelation(Guid correlationId) : ICorrelationContext
    {
        public Guid CorrelationId { get; } = correlationId;
    }
}
