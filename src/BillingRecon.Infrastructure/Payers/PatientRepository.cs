using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>Loads patients for a change and adds new ones, through the request's context: filtered by its tenant, saved by its unit of work.</summary>
public sealed class PatientRepository(BillingReconDbContext context) : IPatientRepository
{
    /// <inheritdoc />
    public Task<Patient?> GetAsync(PatientId id, CancellationToken ct)
        => context.Set<Patient>().SingleOrDefaultAsync(patient => patient.Id == id, ct);

    /// <inheritdoc />
    public void Add(Patient patient) => context.Set<Patient>().Add(patient);
}
