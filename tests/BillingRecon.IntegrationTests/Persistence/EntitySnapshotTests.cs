using System.Text.Json;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The before and after images of an audit record (BR-701): the stored columns of the aggregate, as the columns hold
/// them, with the children of the aggregate. Tested without a database: the images come from the change tracker.
/// </summary>
public sealed class EntitySnapshotTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    [Fact]
    public void A_new_aggregate_has_no_before_image_and_an_after_image_of_its_stored_columns()
    {
        var identity = FixedIdentity.Random();
        using var context = TestContexts.CreateProduction(identity);
        var patient = Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma", DateOfBirth = new DateOnly(1988, 4, 9), Sex = Sex.Female });
        var entry = context.Add(patient);
        context.BeginCommit();

        EntitySnapshot.Before(context, entry).ShouldBeNull();
        var after = JsonDocument.Parse(EntitySnapshot.After(context, entry)).RootElement;

        after.GetProperty("Id").GetGuid().ShouldBe(patient.Id.Value, "a typed id is stored as its Guid");
        after.GetProperty("Mrn").GetString().ShouldBe("MRN-1");
        after.GetProperty("FullName").GetString().ShouldBe("Priya Sharma");
        after.GetProperty("DateOfBirth").GetString().ShouldBe("1988-04-09");
        after.GetProperty("Sex").GetString().ShouldBe("Female", "an enum is stored by name");
        after.GetProperty("Status").GetString().ShouldBe("Active");
        after.GetProperty("Phone").ValueKind.ShouldBe(JsonValueKind.Null);
        after.GetProperty("CreatedByUserId").GetGuid().ShouldBe(identity.UserId!.Value.Value, "the image is taken after the audit columns are stamped");
        after.TryGetProperty("RowVersion", out _).ShouldBeFalse("the database assigns the row version after the save");
        after.TryGetProperty("TenantId", out _).ShouldBeFalse("the audit record carries the tenant in its own column");
        after.TryGetProperty("DomainEvents", out _).ShouldBeFalse();
        EntitySnapshot.IdOf(entry).ShouldBe(patient.Id.Value);
    }

    [Fact]
    public void A_changed_aggregate_has_the_values_it_was_read_with_before_and_the_new_ones_after()
    {
        var identity = FixedIdentity.Random();
        using var context = TestContexts.CreateProduction(identity);
        var patient = context.TrackExisting(Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma", Phone = "+91 98765 43210" }), identity.TenantId!.Value);

        patient.UpdateDemographics(new PatientDetails { FullName = "Priya S. Rao" });
        context.BeginCommit();
        var entry = context.Entry(patient);

        var before = JsonDocument.Parse(EntitySnapshot.Before(context, entry).ShouldNotBeNull()).RootElement;
        var after = JsonDocument.Parse(EntitySnapshot.After(context, entry)).RootElement;

        before.GetProperty("FullName").GetString().ShouldBe("Priya Sharma");
        before.GetProperty("Phone").GetString().ShouldBe("+91 98765 43210");
        after.GetProperty("FullName").GetString().ShouldBe("Priya S. Rao");
        after.GetProperty("Phone").ValueKind.ShouldBe(JsonValueKind.Null);
        after.GetProperty("Mrn").GetString().ShouldBe(before.GetProperty("Mrn").GetString());
    }

    [Fact]
    public void A_bill_carries_its_lines_with_money_as_numbers_and_without_the_key_of_the_bill()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        var bill = NewBill(Consultation(), Dressing());
        var entry = context.Add(bill);
        context.BeginCommit();

        var after = JsonDocument.Parse(EntitySnapshot.After(context, entry)).RootElement;

        after.GetProperty("Status").GetString().ShouldBe("Draft");
        after.GetProperty("Total").GetDecimal().ShouldBe(716.00m);
        after.GetProperty("BillDate").GetString().ShouldBe("2026-11-20");
        var lines = after.GetProperty("Lines").EnumerateArray().OrderBy(line => line.GetProperty("LineNo").GetInt32()).ToArray();
        lines.Length.ShouldBe(2);
        lines[0].GetProperty("Description").GetString().ShouldBe("OPD consultation");
        lines[0].GetProperty("LineTotal").GetDecimal().ShouldBe(590.00m);
        lines[1].GetProperty("Quantity").GetDecimal().ShouldBe(2m);
        lines[1].GetProperty("ServiceItemId").ValueKind.ShouldBe(JsonValueKind.Null);
        lines[0].TryGetProperty("BillId", out _).ShouldBeFalse("a line is inside its bill");
        lines[0].TryGetProperty("TenantId", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_line_that_was_removed_is_in_the_before_image_and_not_in_the_after_image()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        var bill = NewBill(Consultation(), Dressing());
        var entry = context.Add(bill);
        context.BeginCommit();

        // As if the bill had been saved and read back: every row unchanged, original values equal to the current ones.
        context.ChangeTracker.AcceptAllChanges();
        bill.ReplaceLines([Consultation() with { Description = "OPD consultation, senior" }]).IsSuccess.ShouldBeTrue();
        context.BeginCommit();

        var before = JsonDocument.Parse(EntitySnapshot.Before(context, entry).ShouldNotBeNull()).RootElement;
        var after = JsonDocument.Parse(EntitySnapshot.After(context, entry)).RootElement;

        before.GetProperty("Lines").EnumerateArray().Select(line => line.GetProperty("Description").GetString())
            .ShouldBe(["OPD consultation", "Dressing"], ignoreOrder: true);
        after.GetProperty("Lines").EnumerateArray().Select(line => line.GetProperty("Description").GetString())
            .ShouldBe(["OPD consultation, senior"]);
        before.GetProperty("Total").GetDecimal().ShouldBe(716.00m);
        after.GetProperty("Total").GetDecimal().ShouldBe(590.00m);
    }

    [Fact]
    public void Text_outside_ascii_is_stored_as_itself()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        var entry = context.Add(Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "प्रिया शर्मा" }));
        context.BeginCommit();

        EntitySnapshot.After(context, entry).ShouldContain("\"FullName\":\"प्रिया शर्मा\"");
    }

    private static Bill NewBill(params BillLineDetails[] lines) => Bill.Create(
        new PatientId(Guid.NewGuid()),
        new PayerId(Guid.NewGuid()),
        new BillHeader { BillDate = Today, DueDate = Today },
        lines,
        new BusinessDateWindow(Today, 7)).Value;

    // 1 x 500.00 at 18%: 590.00.
    private static BillLineDetails Consultation()
        => new() { ServiceItemId = new ServiceItemId(Guid.NewGuid()), Description = "OPD consultation", Quantity = Quantity.Of(1m), UnitPrice = UnitPrice.Of(500m), TaxRate = Percent.Of(18m) };

    // 2 x 60.00 at 5%: 126.00.
    private static BillLineDetails Dressing()
        => new() { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) };
}
