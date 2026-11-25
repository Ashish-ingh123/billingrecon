using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>
/// What a printed document needs from the domain (ADR 0054): the hospital's letterhead on the tenant, the SAC code on a
/// service item and on the bill line written from it, and the record of a print.
/// </summary>
public sealed class PrintedDocumentsTests
{
    private static readonly DateTimeOffset Now = new(2027, 4, 2, 5, 0, 0, TimeSpan.Zero);
    private static readonly TenantLetterhead Northfield = new()
    {
        LegalName = "  Northfield Care Hospital Private Limited ",
        AddressLine1 = "12 Example Road",
        AddressLine2 = " ",
        City = "Pune",
        State = "Maharashtra",
        PinCode = "411001",
        Phone = "+91 00000 00000",
        Email = "accounts@northfield.example",
        Gstin = "27AAAAA0000A1Z5",
        Signatory = "Authorised signatory",
    };

    [Fact]
    public void ADR0054_a_tenant_has_no_letterhead_until_one_is_set()
    {
        var tenant = Tenant.Create("northfield", "Northfield Care Hospital", Now).Value;

        tenant.Letterhead.ShouldBe(new TenantLetterhead());
    }

    [Fact]
    public void ADR0054_the_letterhead_is_set_trimmed_with_blanks_as_none_and_only_a_change_is_an_event()
    {
        var tenant = Tenant.Create("northfield", "Northfield Care Hospital", Now).Value;
        tenant.ClearDomainEvents();

        tenant.ChangeLetterhead(Northfield).ShouldBeTrue();

        tenant.LegalName.ShouldBe("Northfield Care Hospital Private Limited");
        tenant.AddressLine2.ShouldBeNull("a blank part is none");
        (tenant.City, tenant.State, tenant.PinCode, tenant.Gstin).ShouldBe(("Pune", "Maharashtra", "411001", "27AAAAA0000A1Z5"));
        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBe(new TenantLetterheadChanged("Northfield Care Hospital Private Limited", "Maharashtra", "27AAAAA0000A1Z5"));

        // The same letterhead again changes nothing and says nothing.
        tenant.ChangeLetterhead(Northfield).ShouldBeFalse();
        tenant.DomainEvents.Count.ShouldBe(1);

        // Leaving a part out clears it.
        tenant.ChangeLetterhead(Northfield with { Gstin = null, Phone = "" }).ShouldBeTrue();
        (tenant.Gstin, tenant.Phone, tenant.City).ShouldBe((null, null, "Pune"));
    }

    [Theory]
    [InlineData("27AAAAA0000A1Z")]
    [InlineData("27AAAAA0000A1Z55")]
    [InlineData("27aaaaa0000a1z5")]
    [InlineData("27AAAAA0000A1Z-")]
    public void ADR0054_a_gstin_that_is_not_fifteen_capital_letters_and_digits_is_a_bug_in_the_caller(string gstin)
    {
        var tenant = Tenant.Create("northfield", "Northfield Care Hospital", Now).Value;

        Tenant.IsGstin(gstin).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => tenant.ChangeLetterhead(Northfield with { Gstin = gstin }));
        tenant.Letterhead.ShouldBe(new TenantLetterhead(), "a letterhead that is refused leaves the tenant as it was");
    }

    [Fact]
    public void ADR0054_a_service_item_carries_its_sac_code_and_a_change_of_it_is_an_update()
    {
        var item = ServiceItem.Create(ServiceItemCode.Of("LAB-CBC"), Details("999311"));
        item.SacCode.ShouldBe("999311");
        item.ClearDomainEvents();

        item.Update(Details("999311"));
        item.DomainEvents.ShouldBeEmpty();

        item.Update(Details(sacCode: null));
        item.SacCode.ShouldBeNull();
        item.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ServiceItemUpdated>();

        Should.Throw<ArgumentException>(() => item.Update(Details("99931")));
        Should.Throw<ArgumentException>(() => item.Update(Details("99931A")));
        (ServiceItem.IsSacCode("999311"), ServiceItem.IsSacCode("9993111"), ServiceItem.IsSacCode(null)).ShouldBe((true, false, false));
    }

    [Fact]
    public void ADR0054_a_bill_line_keeps_the_sac_code_it_was_written_with()
    {
        var today = new DateOnly(2027, 4, 2);
        var lines = new[]
        {
            new BillLineDetails { ServiceItemId = new ServiceItemId(Guid.NewGuid()), Description = "Complete blood count", Quantity = Quantity.Of(1m), UnitPrice = UnitPrice.Of(350m), TaxRate = Percent.Zero, SacCode = "999311" },
            new BillLineDetails { Description = "Consultation", Quantity = Quantity.Of(1m), UnitPrice = UnitPrice.Of(500m), TaxRate = Percent.Zero },
        };

        var bill = Bill.Create(new PatientId(Guid.NewGuid()), new PayerId(Guid.NewGuid()), new BillHeader { BillDate = today, DueDate = today }, lines, new BusinessDateWindow(today, 7)).Value;

        bill.Lines.Select(line => line.SacCode).ShouldBe(["999311", null]);
    }

    [Fact]
    public void ADR0054_the_first_print_of_a_document_is_its_original_and_every_later_one_a_duplicate()
    {
        var document = Guid.NewGuid();
        var user = new UserId(Guid.NewGuid());

        var first = DocumentPrint.Record(PrintedDocumentType.Bill, document, 1, Now, user);
        var second = DocumentPrint.Record(PrintedDocumentType.Bill, document, 2, Now.AddMinutes(5), printedBy: null);

        (first.IsOriginal, first.CopyNumber, first.PrintedAtUtc, first.PrintedByUserId).ShouldBe((true, 1, Now, user));
        (second.IsOriginal, second.CopyNumber, second.PrintedByUserId).ShouldBe((false, 2, null));
        typeof(IAppendOnly).IsAssignableFrom(typeof(DocumentPrint)).ShouldBeTrue("a print is recorded once and never changed");

        Should.Throw<ArgumentException>(() => DocumentPrint.Record(PrintedDocumentType.Bill, Guid.Empty, 1, Now, user));
        Should.Throw<ArgumentOutOfRangeException>(() => DocumentPrint.Record(PrintedDocumentType.Bill, document, 0, Now, user));
        Should.Throw<ArgumentException>(() => DocumentPrint.Record((PrintedDocumentType)9, document, 1, Now, user));
    }

    private static ServiceItemDetails Details(string? sacCode)
        => new() { Name = "Complete blood count", DefaultUnitPrice = UnitPrice.Of(350m), TaxRate = Percent.Zero, SacCode = sacCode };
}
