using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>The parties a text names, as queries on the context of the request. The tenant filter of the context applies.</summary>
public sealed class PartySearch(BillingReconDbContext context) : IPartySearch
{
    /// <inheritdoc />
    public IQueryable<PatientId> PatientsNamed(string text) => Patients(LikePattern.Containing(text)).Select(patient => patient.Id);

    /// <inheritdoc />
    public IQueryable<PayerId> PayersNamed(string text)
    {
        var pattern = LikePattern.Containing(text);
        var patients = Patients(pattern);

        return context.Set<Payer>()
            .Where(payer =>
                EF.Functions.Like(payer.Name, pattern, LikePattern.Escape)
                || patients.Any(patient => (PatientId?)patient.Id == payer.PatientId))
            .Select(payer => payer.Id);
    }

    // The MRN is a value object stored through a converter: the cast names its column (as PatientReadService does).
    private IQueryable<Patient> Patients(string pattern)
        => context.Set<Patient>().Where(patient =>
            EF.Functions.Like((string)(object)patient.Mrn, pattern, LikePattern.Escape)
            || EF.Functions.Like(patient.FullName, pattern, LikePattern.Escape));
}
