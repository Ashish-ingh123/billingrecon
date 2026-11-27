using System.Data.Common;
using System.Globalization;
using System.Text;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Operations;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The reconciliation of every hospital, as the schedule runs it each night (ADR 0056): the query of the report
/// (<see cref="ReconciliationQuery"/>, ADR 0005), once for each hospital, suspended ones included, each in a context
/// of its own as that hospital's operator. Drift anywhere, or a hospital that could not be reconciled, raises one alert
/// that names every such hospital by its code; a clean night raises none and writes one line of information.
/// </summary>
public sealed partial class NightlyReconciliation(
    DbContextOptions<BillingReconDbContext> options,
    IClock clock,
    IOperationsAlerts alerts,
    ILogger<NightlyReconciliation> logger)
{
    /// <summary>The rows of one hospital an alert lists at most; the report has them all.</summary>
    public const int RowsPerHospital = 20;

    /// <summary>The subject of the alert of a night with drift.</summary>
    public const string DriftSubject = "Reconciliation drift";

    /// <summary>Reconciles every hospital; answers what was found, by hospital code.</summary>
    public async Task<ReconciliationNight> RunAsync(CancellationToken ct)
    {
        List<(TenantId Id, string Code)> tenants;
        await using (var system = new BillingReconDbContext(options, SystemIdentity.Instance, SystemIdentity.Instance, clock))
        {
            tenants = [.. (await system.Set<Tenant>().AsNoTracking()
                    .OrderBy(tenant => tenant.Code)
                    .Select(tenant => new { tenant.Id, tenant.Code })
                    .ToListAsync(ct))
                .Select(tenant => (tenant.Id, tenant.Code))];
        }

        var drifted = new Dictionary<string, IReadOnlyList<DriftRow>>(StringComparer.Ordinal);
        var failed = new List<string>();
        foreach (var (tenantId, code) in tenants)
        {
            try
            {
                var identity = new OperatorIdentity(tenantId);
                await using var context = new BillingReconDbContext(options, identity, identity, clock);
                var drift = await new ReconciliationQuery(context, identity).FindDriftAsync(ct);
                if (drift.Count > 0)
                {
                    drifted[code] = drift;
                }
            }
            catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
            {
                LogHospitalFailed(logger, exception, code);
                failed.Add(code);
            }
        }

        var night = new ReconciliationNight(tenants.Count, drifted, failed);
        if (drifted.Count > 0 || failed.Count > 0)
        {
            alerts.Raise(DriftSubject, Describe(night));
        }
        else
        {
            LogClean(logger, tenants.Count);
        }

        return night;
    }

    // The text of the alert: what to do first, and each hospital with what was found.
    private string Describe(ReconciliationNight night)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"The reconciliation of {clock.UtcNow:yyyy-MM-dd HH:mm} UTC checked {night.Hospitals} hospitals.").Append("\r\n");
        text.Append("Follow docs/runbook.md section 16.1 (reconciliation drift, Sev-1) before anyone works on the hospitals named here.").Append("\r\n");
        foreach (var (code, rows) in night.Drift)
        {
            text.Append("\r\n").Append(CultureInfo.InvariantCulture, $"Hospital {code}: {rows.Count} rows of drift.").Append("\r\n");
            foreach (var row in rows.Take(RowsPerHospital))
            {
                text.Append(CultureInfo.InvariantCulture, $"  {row.Identity} {row.Reference ?? row.SubjectId.ToString()}: cached {row.Cached:0.00}, ledger {row.Ledger:0.00}").Append("\r\n");
            }

            if (rows.Count > RowsPerHospital)
            {
                text.Append(CultureInfo.InvariantCulture, $"  and {rows.Count - RowsPerHospital} more: see the reconciliation report.").Append("\r\n");
            }
        }

        foreach (var code in night.Failed)
        {
            text.Append("\r\n").Append(CultureInfo.InvariantCulture, $"Hospital {code}: could not be reconciled; the error is in the log.").Append("\r\n");
        }

        return text.ToString();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The nightly reconciliation of {Hospitals} hospitals found no drift")]
    private static partial void LogClean(ILogger logger, int hospitals);

    [LoggerMessage(Level = LogLevel.Error, Message = "The nightly reconciliation of hospital {HospitalCode} failed")]
    private static partial void LogHospitalFailed(ILogger logger, Exception exception, string hospitalCode);
}
