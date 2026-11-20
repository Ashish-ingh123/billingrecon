using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// The ServiceItem aggregate (docs/domain-model.md, section 4.3): a code that never changes, a price and a tax rate
/// that can be corrected, never deleted.
/// </summary>
public sealed class ServiceItemTests
{
    private static readonly ServiceItemCode Code = ServiceItemCode.Of("CONS-OPD");

    [Fact]
    public void A_new_item_is_active_and_carries_what_was_entered()
    {
        var item = ServiceItem.Create(Code, Details());

        item.Code.ShouldBe(Code);
        item.Name.ShouldBe("OPD consultation");
        item.Category.ShouldBe("Consultation");
        item.DefaultUnitPrice.ShouldBe(UnitPrice.Of(500m));
        item.TaxRate.ShouldBe(Percent.Of(18m));
        item.Status.ShouldBe(ServiceItemStatus.Active);
    }

    [Fact]
    public void BR201_a_free_untaxed_service_and_a_price_with_four_decimals_are_valid()
    {
        var free = ServiceItem.Create(Code, Details() with { DefaultUnitPrice = UnitPrice.Zero, TaxRate = Percent.Zero });
        var precise = ServiceItem.Create(Code, Details() with { DefaultUnitPrice = UnitPrice.Of(333.3333m), TaxRate = Percent.Of(2.5m) });

        free.DefaultUnitPrice.Value.ShouldBe(0m);
        free.TaxRate.Value.ShouldBe(0m);
        precise.DefaultUnitPrice.Value.ShouldBe(333.3333m);
        precise.TaxRate.Value.ShouldBe(2.5m);
    }

    [Fact]
    public void Creating_raises_ServiceItemCreated_with_the_code_the_name_the_price_and_the_tax_rate()
    {
        var item = ServiceItem.Create(Code, Details());

        item.DomainEvents.ShouldHaveSingleItem()
            .ShouldBe(new ServiceItemCreated(Code, "OPD consultation", UnitPrice.Of(500m), Percent.Of(18m)));
    }

    [Fact]
    public void A_new_item_has_no_id_no_tenant_and_no_row_version_until_it_is_saved()
    {
        var item = ServiceItem.Create(Code, Details());

        item.Id.ShouldBe(default);
        item.TenantId.ShouldBe(default);
        item.RowVersion.ShouldBeEmpty();
    }

    [Fact]
    public void An_item_without_a_code_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => ServiceItem.Create(default, Details())).ParamName.ShouldBe("code");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_item_without_a_name_is_a_bug_and_throws(string name)
    {
        Should.Throw<ArgumentException>(() => ServiceItem.Create(Code, Details() with { Name = name }));
    }

    [Fact]
    public void A_name_or_a_category_longer_than_its_column_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => ServiceItem.Create(Code, Details() with { Name = new string('n', ServiceItem.NameMaxLength + 1) }));
        Should.Throw<ArgumentException>(() => ServiceItem.Create(Code, Details() with { Category = new string('c', ServiceItem.CategoryMaxLength + 1) }));
    }

    [Fact]
    public void Text_is_stored_without_surrounding_white_space_and_a_blank_category_as_null()
    {
        var item = ServiceItem.Create(Code, Details() with { Name = "  OPD consultation ", Category = "  " });

        item.Name.ShouldBe("OPD consultation");
        item.Category.ShouldBeNull();
    }

    [Fact]
    public void Updating_replaces_the_name_the_category_the_price_and_the_tax_rate_and_keeps_the_code_and_the_status()
    {
        var item = ServiceItem.Create(Code, Details());
        item.Deactivate();

        item.Update(new ServiceItemDetails { Name = "OPD consultation (senior)", DefaultUnitPrice = UnitPrice.Of(750.5m), TaxRate = Percent.Of(5m) });

        item.Name.ShouldBe("OPD consultation (senior)");
        item.Category.ShouldBeNull("a category the correction leaves out is cleared");
        item.DefaultUnitPrice.ShouldBe(UnitPrice.Of(750.5m));
        item.TaxRate.ShouldBe(Percent.Of(5m));
        item.Code.ShouldBe(Code);
        item.Status.ShouldBe(ServiceItemStatus.Inactive);
    }

    [Fact]
    public void Updating_raises_ServiceItemUpdated_when_a_value_changed_and_nothing_when_none_did()
    {
        var item = ServiceItem.Create(Code, Details());
        item.ClearDomainEvents();

        item.Update(Details());
        item.DomainEvents.ShouldBeEmpty("the same details change nothing");

        item.Update(Details() with { DefaultUnitPrice = UnitPrice.Of(550m) });
        item.DomainEvents.ShouldHaveSingleItem().ShouldBe(new ServiceItemUpdated(item.Id, Code, "OPD consultation"));
    }

    [Fact]
    public void Details_that_are_refused_leave_the_item_as_it_was()
    {
        var item = ServiceItem.Create(Code, Details());

        Should.Throw<ArgumentException>(() => item.Update(Details() with
        {
            Name = "Changed",
            Category = new string('c', ServiceItem.CategoryMaxLength + 1),
            DefaultUnitPrice = UnitPrice.Of(1m),
        }));

        item.Name.ShouldBe("OPD consultation");
        item.DefaultUnitPrice.ShouldBe(UnitPrice.Of(500m));
    }

    [Fact]
    public void Deactivating_an_item_makes_it_inactive_and_raises_ServiceItemDeactivated()
    {
        var item = ServiceItem.Create(Code, Details());
        item.ClearDomainEvents();

        item.Deactivate().IsSuccess.ShouldBeTrue();

        item.Status.ShouldBe(ServiceItemStatus.Inactive);
        item.DomainEvents.ShouldHaveSingleItem().ShouldBe(new ServiceItemDeactivated(item.Id, Code, "OPD consultation"));
    }

    [Fact]
    public void Deactivating_an_inactive_item_fails_and_raises_nothing()
    {
        var item = ServiceItem.Create(Code, Details());
        item.Deactivate();
        item.ClearDomainEvents();

        var result = item.Deactivate();

        result.Error.Code.ShouldBe(BillingErrors.ServiceItemAlreadyInactiveCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        item.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Reactivating_an_inactive_item_makes_it_active_and_raises_ServiceItemReactivated()
    {
        var item = ServiceItem.Create(Code, Details());
        item.Deactivate();
        item.ClearDomainEvents();

        item.Reactivate().IsSuccess.ShouldBeTrue();

        item.Status.ShouldBe(ServiceItemStatus.Active);
        item.DomainEvents.ShouldHaveSingleItem().ShouldBe(new ServiceItemReactivated(item.Id, Code, "OPD consultation"));
    }

    [Fact]
    public void Reactivating_an_active_item_fails_and_raises_nothing()
    {
        var item = ServiceItem.Create(Code, Details());
        item.ClearDomainEvents();

        item.Reactivate().Error.Code.ShouldBe(BillingErrors.ServiceItemAlreadyActiveCode);
        item.DomainEvents.ShouldBeEmpty();
    }

    private static ServiceItemDetails Details() => new()
    {
        Name = "OPD consultation",
        Category = "Consultation",
        DefaultUnitPrice = UnitPrice.Of(500m),
        TaxRate = Percent.Of(18m),
    };
}
