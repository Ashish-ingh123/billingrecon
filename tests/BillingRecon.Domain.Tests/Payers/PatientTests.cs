using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Domain.Tests.Payers;

/// <summary>The Patient aggregate (BR-101, BR-102): registration, demographics, deactivation and its events.</summary>
public sealed class PatientTests
{
    private static readonly Mrn SomeMrn = Mrn.Of("MRN-000123");

    [Fact]
    public void BR101_a_registered_patient_keeps_the_mrn_and_its_demographics_and_is_active()
    {
        var patient = Patient.Register(SomeMrn, CompleteDetails());

        patient.Mrn.ShouldBe(SomeMrn);
        patient.FullName.ShouldBe("Priya Sharma");
        patient.DateOfBirth.ShouldBe(new DateOnly(1990, 5, 12));
        patient.Sex.ShouldBe(Sex.Female);
        patient.Phone.ShouldBe("+91 98765 43210");
        patient.Email.ShouldBe("priya.sharma@example.com");
        patient.AddressLine1.ShouldBe("12 MG Road");
        patient.AddressLine2.ShouldBe("Camp");
        patient.City.ShouldBe("Pune");
        patient.State.ShouldBe("Maharashtra");
        patient.PinCode.ShouldBe("411001");
        patient.Status.ShouldBe(PatientStatus.Active);
    }

    [Fact]
    public void A_new_patient_has_no_id_no_tenant_and_no_row_version_until_it_is_saved()
    {
        // The persistence layer assigns all three (ADR 0015); the aggregate never does.
        var patient = Patient.Register(SomeMrn, MinimalDetails());

        patient.Id.ShouldBe(default);
        patient.TenantId.ShouldBe(default);
        patient.RowVersion.ShouldBeEmpty();
    }

    [Fact]
    public void Registering_raises_PatientRegistered_with_the_mrn()
    {
        var patient = Patient.Register(SomeMrn, MinimalDetails());

        patient.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PatientRegistered(SomeMrn));
    }

    [Fact]
    public void Text_is_stored_without_surrounding_white_space_and_a_blank_optional_value_as_null()
    {
        var patient = Patient.Register(SomeMrn, new PatientDetails
        {
            FullName = "  Priya Sharma  ",
            Phone = "   ",
            Email = string.Empty,
            City = " Pune ",
            PinCode = " 411001 ",
        });

        patient.FullName.ShouldBe("Priya Sharma");
        patient.Phone.ShouldBeNull();
        patient.Email.ShouldBeNull();
        patient.City.ShouldBe("Pune");
        patient.PinCode.ShouldBe("411001");
        patient.AddressLine1.ShouldBeNull();
    }

    [Fact]
    public void The_default_mrn_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => Patient.Register(default, MinimalDetails())).ParamName.ShouldBe("mrn");
    }

    [Theory]
    [InlineData(nameof(PatientDetails.FullName))]
    [InlineData(nameof(PatientDetails.Phone))]
    [InlineData(nameof(PatientDetails.Email))]
    [InlineData(nameof(PatientDetails.AddressLine1))]
    [InlineData(nameof(PatientDetails.AddressLine2))]
    [InlineData(nameof(PatientDetails.City))]
    [InlineData(nameof(PatientDetails.State))]
    [InlineData(nameof(PatientDetails.PinCode))]
    public void A_value_beyond_its_limit_is_a_bug_and_throws_naming_the_field_not_the_value(string field)
    {
        // The validator of the use case refuses such a value first; reaching the aggregate with it is a bug.
        var tooLong = new string('x', 300);
        var details = field switch
        {
            nameof(PatientDetails.FullName) => MinimalDetails() with { FullName = tooLong },
            nameof(PatientDetails.Phone) => MinimalDetails() with { Phone = tooLong },
            nameof(PatientDetails.Email) => MinimalDetails() with { Email = tooLong },
            nameof(PatientDetails.AddressLine1) => MinimalDetails() with { AddressLine1 = tooLong },
            nameof(PatientDetails.AddressLine2) => MinimalDetails() with { AddressLine2 = tooLong },
            nameof(PatientDetails.City) => MinimalDetails() with { City = tooLong },
            nameof(PatientDetails.State) => MinimalDetails() with { State = tooLong },
            _ => MinimalDetails() with { PinCode = tooLong },
        };

        var exception = Should.Throw<ArgumentException>(() => Patient.Register(SomeMrn, details));

        exception.ParamName.ShouldBe($"details.{field}");
        exception.Message.ShouldNotContain(tooLong);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_patient_without_a_full_name_is_a_bug_and_throws(string fullName)
    {
        Should.Throw<ArgumentException>(() => Patient.Register(SomeMrn, MinimalDetails() with { FullName = fullName }))
            .ParamName.ShouldBe("details.FullName");
    }

    [Fact]
    public void A_pin_code_outside_ascii_is_a_bug_and_throws_because_its_column_is_varchar()
    {
        Should.Throw<ArgumentException>(() => Patient.Register(SomeMrn, MinimalDetails() with { PinCode = "४११००१" }));
    }

    [Fact]
    public void A_sex_the_enum_does_not_define_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Patient.Register(SomeMrn, MinimalDetails() with { Sex = (Sex)42 }));
    }

    [Fact]
    public void Updating_the_demographics_replaces_every_field_and_clears_what_was_left_out()
    {
        var patient = Patient.Register(SomeMrn, CompleteDetails());

        patient.UpdateDemographics(new PatientDetails { FullName = "Priya S. Rao", City = "Mumbai" });

        patient.FullName.ShouldBe("Priya S. Rao");
        patient.City.ShouldBe("Mumbai");
        patient.DateOfBirth.ShouldBeNull();
        patient.Sex.ShouldBeNull();
        patient.Phone.ShouldBeNull();
        patient.Email.ShouldBeNull();
        patient.AddressLine1.ShouldBeNull();
        patient.AddressLine2.ShouldBeNull();
        patient.State.ShouldBeNull();
        patient.PinCode.ShouldBeNull();
    }

    [Fact]
    public void Updating_the_demographics_leaves_the_mrn_and_the_status_alone_and_raises_PatientUpdated()
    {
        // Until Prompt 3.4 an update raised no event; the audit trail needs one for every state change (BR-701).
        var patient = Patient.Register(SomeMrn, CompleteDetails());
        patient.ClearDomainEvents();

        patient.UpdateDemographics(MinimalDetails() with { FullName = "Someone Else" });

        patient.Mrn.ShouldBe(SomeMrn);
        patient.Status.ShouldBe(PatientStatus.Active);
        patient.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PatientUpdated(patient.Id, SomeMrn));
    }

    [Fact]
    public void Updating_with_the_demographics_the_patient_already_has_changes_nothing_and_raises_nothing()
    {
        var patient = Patient.Register(SomeMrn, CompleteDetails());
        patient.ClearDomainEvents();

        patient.UpdateDemographics(CompleteDetails());

        patient.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Refused_details_leave_the_patient_as_it_was()
    {
        var patient = Patient.Register(SomeMrn, CompleteDetails());

        // The phone is valid, the city is not: nothing is applied.
        Should.Throw<ArgumentException>(() => patient.UpdateDemographics(new PatientDetails
        {
            FullName = "Changed",
            Phone = "+91 90000 00000",
            City = new string('c', Patient.CityMaxLength + 1),
        }));

        patient.FullName.ShouldBe("Priya Sharma");
        patient.Phone.ShouldBe("+91 98765 43210");
        patient.City.ShouldBe("Pune");
    }

    [Fact]
    public void BR102_deactivating_a_patient_makes_them_inactive_and_raises_PatientDeactivated()
    {
        var patient = Patient.Register(SomeMrn, MinimalDetails());
        patient.ClearDomainEvents();

        var result = patient.Deactivate();

        result.IsSuccess.ShouldBeTrue();
        patient.Status.ShouldBe(PatientStatus.Inactive);
        patient.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PatientDeactivated(patient.Id, SomeMrn));
    }

    [Fact]
    public void BR102_deactivating_an_inactive_patient_fails_and_raises_nothing()
    {
        var patient = Patient.Register(SomeMrn, MinimalDetails());
        patient.Deactivate();
        patient.ClearDomainEvents();

        var result = patient.Deactivate();

        result.Error.Code.ShouldBe(PayersErrors.PatientAlreadyInactiveCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        patient.Status.ShouldBe(PatientStatus.Inactive);
        patient.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Reactivating_an_inactive_patient_makes_them_active_and_raises_PatientReactivated()
    {
        var patient = Patient.Register(SomeMrn, MinimalDetails());
        patient.Deactivate();
        patient.ClearDomainEvents();

        var result = patient.Reactivate();

        result.IsSuccess.ShouldBeTrue();
        patient.Status.ShouldBe(PatientStatus.Active);
        patient.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PatientReactivated(patient.Id, SomeMrn));
    }

    [Fact]
    public void Reactivating_an_active_patient_fails_and_raises_nothing()
    {
        var patient = Patient.Register(SomeMrn, MinimalDetails());
        patient.ClearDomainEvents();

        var result = patient.Reactivate();

        result.Error.Code.ShouldBe(PayersErrors.PatientAlreadyActiveCode);
        patient.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void BR102_the_demographics_of_an_inactive_patient_can_still_be_corrected()
    {
        var patient = Patient.Register(SomeMrn, MinimalDetails());
        patient.Deactivate();

        patient.UpdateDemographics(MinimalDetails() with { FullName = "Corrected Name" });

        patient.FullName.ShouldBe("Corrected Name");
        patient.Status.ShouldBe(PatientStatus.Inactive);
    }

    private static PatientDetails MinimalDetails() => new() { FullName = "Priya Sharma" };

    private static PatientDetails CompleteDetails() => new()
    {
        FullName = "Priya Sharma",
        DateOfBirth = new DateOnly(1990, 5, 12),
        Sex = Sex.Female,
        Phone = "+91 98765 43210",
        Email = "priya.sharma@example.com",
        AddressLine1 = "12 MG Road",
        AddressLine2 = "Camp",
        City = "Pune",
        State = "Maharashtra",
        PinCode = "411001",
    };
}
