using BillingRecon.Application.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Explicit mapping between the Payers aggregates and what the Application layer takes in and hands out (CLAUDE.md
/// section 5): the details of a command become the details an aggregate takes, and an aggregate becomes its DTO.
/// </summary>
public static class PayersMapping
{
    /// <summary>The patient as the API returns it.</summary>
    public static PatientDto ToDto(this Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        return new PatientDto(
            Id: patient.Id.Value,
            Mrn: patient.Mrn.Value,
            FullName: patient.FullName,
            DateOfBirth: patient.DateOfBirth,
            Sex: patient.Sex,
            Phone: patient.Phone,
            Email: patient.Email,
            AddressLine1: patient.AddressLine1,
            AddressLine2: patient.AddressLine2,
            City: patient.City,
            State: patient.State,
            PinCode: patient.PinCode,
            Status: patient.Status,
            CreatedAtUtc: patient.CreatedAtUtc,
            UpdatedAtUtc: patient.UpdatedAtUtc,
            Version: RowVersionText.Of(patient));
    }

    /// <summary>The payer as the API returns it.</summary>
    public static PayerDto ToDto(this Payer payer)
    {
        ArgumentNullException.ThrowIfNull(payer);

        return new PayerDto(
            Id: payer.Id.Value,
            Kind: payer.Kind,
            Name: payer.Name,
            PatientId: payer.PatientId?.Value,
            Phone: payer.Phone,
            Email: payer.Email,
            Status: payer.Status,
            CreatedAtUtc: payer.CreatedAtUtc,
            UpdatedAtUtc: payer.UpdatedAtUtc,
            Version: RowVersionText.Of(payer));
    }

    /// <summary>The demographics of a validated command, as the patient takes them.</summary>
    public static PatientDetails ToDetails(this IPatientDetailsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new PatientDetails
        {
            FullName = input.FullName,
            DateOfBirth = input.DateOfBirth,
            Sex = input.Sex,
            Phone = input.Phone,
            Email = input.Email,
            AddressLine1 = input.AddressLine1,
            AddressLine2 = input.AddressLine2,
            City = input.City,
            State = input.State,
            PinCode = input.PinCode,
        };
    }

    /// <summary>The name and contact details of a validated command, as the payer takes them.</summary>
    public static PayerDetails ToDetails(this IPayerDetailsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new PayerDetails
        {
            Name = input.Name,
            Phone = input.Phone,
            Email = input.Email,
        };
    }
}
