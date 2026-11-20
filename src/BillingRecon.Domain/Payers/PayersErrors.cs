using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>The failures of the Payers module (BR-101 to BR-105). The codes are part of the API contract (docs/api.md).</summary>
public static class PayersErrors
{
    public const string PatientNotFoundCode = "patient_not_found";
    public const string PatientMrnDuplicateCode = "patient_mrn_duplicate";
    public const string PatientInactiveCode = "patient_inactive";
    public const string PatientAlreadyLinkedCode = "patient_already_linked";
    public const string PatientAlreadyInactiveCode = "patient_already_inactive";
    public const string PatientAlreadyActiveCode = "patient_already_active";
    public const string PayerNotFoundCode = "payer_not_found";
    public const string PayerInactiveCode = "payer_inactive";
    public const string PayerKindNotSupportedCode = "payer_kind_not_supported";
    public const string PayerAlreadyInactiveCode = "payer_already_inactive";
    public const string PayerAlreadyActiveCode = "payer_already_active";

    /// <summary>No patient with that id exists for the tenant. Another tenant's patient gets the same answer.</summary>
    public static Error PatientNotFound() => Error.NotFound(PatientNotFoundCode, "The patient does not exist.");

    /// <summary>The tenant already has a patient with that MRN (BR-101).</summary>
    public static Error PatientMrnDuplicate()
        => Error.Conflict(PatientMrnDuplicateCode, "A patient with this MRN is already registered (BR-101).");

    /// <summary>The patient is inactive, so no new payer may be linked to them.</summary>
    public static Error PatientInactive()
        => Error.BusinessRule(PatientInactiveCode, "The patient is inactive. Reactivate the patient first (BR-102, BR-104).");

    /// <summary>The patient already has a payer linked to them (BR-104).</summary>
    public static Error PatientAlreadyLinked()
        => Error.Conflict(PatientAlreadyLinkedCode, "The patient already has a self-pay payer (BR-104).");

    /// <summary>Deactivating a patient who is already inactive.</summary>
    public static Error PatientAlreadyInactive() => Error.BusinessRule(PatientAlreadyInactiveCode, "The patient is already inactive.");

    /// <summary>Reactivating a patient who is already active.</summary>
    public static Error PatientAlreadyActive() => Error.BusinessRule(PatientAlreadyActiveCode, "The patient is already active.");

    /// <summary>No payer with that id exists for the tenant. Another tenant's payer gets the same answer.</summary>
    public static Error PayerNotFound() => Error.NotFound(PayerNotFoundCode, "The payer does not exist.");

    /// <summary>The payer is inactive, so it cannot be given a new bill, payment or refund (BR-105).</summary>
    public static Error PayerInactive()
        => Error.BusinessRule(PayerInactiveCode, "The payer is inactive. Reactivate the payer first (BR-105).");

    /// <summary>A payer kind other than self-pay was asked for (BR-103).</summary>
    public static Error PayerKindNotSupported()
        => Error.BusinessRule(
            PayerKindNotSupportedCode,
            "Only self-pay payers exist in this release. Insurers and corporate accounts arrive in R4 (BR-103).");

    /// <summary>Deactivating a payer that is already inactive.</summary>
    public static Error PayerAlreadyInactive() => Error.BusinessRule(PayerAlreadyInactiveCode, "The payer is already inactive.");

    /// <summary>Reactivating a payer that is already active.</summary>
    public static Error PayerAlreadyActive() => Error.BusinessRule(PayerAlreadyActiveCode, "The payer is already active.");
}
