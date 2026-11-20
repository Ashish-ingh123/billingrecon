using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>
/// A patient of the tenant: the person bills are raised for (BR-101, BR-102, BR-106). Registered with the medical
/// record number from the hospital's HIS, which never changes, and demographics, which a clerk corrects. A patient is
/// never deleted, only deactivated: no new bill may then be raised for them, and their existing bills are unaffected.
/// The MRN is unique within the tenant; the registration use case checks it and a unique index enforces it.
/// </summary>
public sealed class Patient : AggregateRoot<PatientId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="FullName"/>.</summary>
    public const int FullNameMaxLength = 200;

    /// <summary>Longest allowed <see cref="Phone"/>.</summary>
    public const int PhoneMaxLength = 20;

    /// <summary>Longest allowed <see cref="Email"/>.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>Longest allowed <see cref="AddressLine1"/> and <see cref="AddressLine2"/>.</summary>
    public const int AddressLineMaxLength = 200;

    /// <summary>Longest allowed <see cref="City"/>.</summary>
    public const int CityMaxLength = 100;

    /// <summary>Longest allowed <see cref="State"/>.</summary>
    public const int StateMaxLength = 100;

    /// <summary>Longest allowed <see cref="PinCode"/>.</summary>
    public const int PinCodeMaxLength = 10;

    // Also the constructor Entity Framework uses to load a patient: it passes the two stored values and then sets the
    // remaining properties.
    private Patient(Mrn mrn, string fullName)
    {
        Mrn = mrn;
        FullName = fullName;
        Status = PatientStatus.Active;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The medical record number from the hospital's HIS (BR-101). It never changes.</summary>
    public Mrn Mrn { get; private set; }

    /// <summary>The full name, as the hospital records it.</summary>
    public string FullName { get; private set; }

    /// <summary>The date of birth, if known.</summary>
    public DateOnly? DateOfBirth { get; private set; }

    /// <summary>The recorded sex, if any.</summary>
    public Sex? Sex { get; private set; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; private set; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; private set; }

    /// <summary>The first line of the address.</summary>
    public string? AddressLine1 { get; private set; }

    /// <summary>The second line of the address.</summary>
    public string? AddressLine2 { get; private set; }

    /// <summary>The city.</summary>
    public string? City { get; private set; }

    /// <summary>The state.</summary>
    public string? State { get; private set; }

    /// <summary>The postal code: the PIN code for an address in India. ASCII only, as its column is.</summary>
    public string? PinCode { get; private set; }

    /// <summary>Whether new bills may be raised for the patient (BR-102).</summary>
    public PatientStatus Status { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>
    /// Registers an active patient. Whether the MRN is free in the tenant is the use case's question (BR-101): it needs
    /// the other patients, which an aggregate does not see.
    /// </summary>
    /// <exception cref="ArgumentException">A value of <paramref name="details"/> breaks a limit the validator checks.</exception>
    public static Patient Register(Mrn mrn, PatientDetails details)
    {
        if (mrn.IsEmpty)
        {
            throw new ArgumentException("The default value of Mrn is not an MRN; build one with Mrn.Of.", nameof(mrn));
        }

        ArgumentNullException.ThrowIfNull(details);

        var patient = new Patient(mrn, Field.Required(details.FullName, FullNameMaxLength));
        patient.Apply(details);

        // The id is still empty here: it is assigned when the patient is added to the unit of work.
        patient.AddDomainEvent(new PatientRegistered(mrn));

        return patient;
    }

    /// <summary>
    /// Replaces the demographics with <paramref name="details"/>: a value left out is cleared. The MRN and the status
    /// are not demographics and stay as they are. An inactive patient's details can be corrected too. Raises
    /// <see cref="PatientUpdated"/> when a value changed; details equal to what the patient already has change nothing.
    /// </summary>
    /// <exception cref="ArgumentException">A value of <paramref name="details"/> breaks a limit the validator checks.</exception>
    public void UpdateDemographics(PatientDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var before = Demographics();
        Apply(details);

        if (before != Demographics())
        {
            AddDomainEvent(new PatientUpdated(Id, Mrn));
        }
    }

    /// <summary>Deactivates the patient (BR-102): no new bill may be raised for them. Their existing bills are unaffected.</summary>
    public Result Deactivate()
    {
        if (Status == PatientStatus.Inactive)
        {
            return PayersErrors.PatientAlreadyInactive();
        }

        Status = PatientStatus.Inactive;
        AddDomainEvent(new PatientDeactivated(Id, Mrn));

        return Result.Success();
    }

    /// <summary>Makes an inactive patient active again, so that bills may be raised for them once more.</summary>
    public Result Reactivate()
    {
        if (Status == PatientStatus.Active)
        {
            return PayersErrors.PatientAlreadyActive();
        }

        Status = PatientStatus.Active;
        AddDomainEvent(new PatientReactivated(Id, Mrn));

        return Result.Success();
    }

    private (string, DateOnly?, Sex?, string?, string?, string?, string?, string?, string?, string?) Demographics()
        => (FullName, DateOfBirth, Sex, Phone, Email, AddressLine1, AddressLine2, City, State, PinCode);

    // Every value is checked before any is assigned, so details that are refused leave the patient as it was.
    private void Apply(PatientDetails details)
    {
        var fullName = Field.Required(details.FullName, FullNameMaxLength);
        var phone = Field.Optional(details.Phone, PhoneMaxLength);
        var email = Field.Optional(details.Email, EmailMaxLength);
        var addressLine1 = Field.Optional(details.AddressLine1, AddressLineMaxLength);
        var addressLine2 = Field.Optional(details.AddressLine2, AddressLineMaxLength);
        var city = Field.Optional(details.City, CityMaxLength);
        var state = Field.Optional(details.State, StateMaxLength);
        var pinCode = Field.Optional(details.PinCode, PinCodeMaxLength);

        if (details.Sex is { } sex && !Enum.IsDefined(sex))
        {
            throw new ArgumentOutOfRangeException(nameof(details), sex, "The sex is not one the enum defines.");
        }

        // The column is varchar: a character outside ASCII would be stored as a question mark.
        if (pinCode is not null && !pinCode.All(char.IsAscii))
        {
            throw new ArgumentException(
                "details.PinCode must be ASCII; the validator of the use case should have refused it.",
                nameof(details));
        }

        FullName = fullName;
        DateOfBirth = details.DateOfBirth;
        Sex = details.Sex;
        Phone = phone;
        Email = email;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        City = city;
        State = state;
        PinCode = pinCode;
    }
}
