using System.Reflection;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

public sealed class TypedIdTests
{
    /// <summary>Every typed id in the domain. The persistence layer converts exactly these types.</summary>
    public static TheoryData<Type> TypedIds()
    {
        var data = new TheoryData<Type>();
        foreach (var type in FindTypedIds())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void The_tenant_and_user_ids_are_among_the_typed_ids_the_tests_below_cover()
    {
        var types = FindTypedIds();

        types.ShouldContain(typeof(TenantId));
        types.ShouldContain(typeof(UserId));
    }

    [Theory]
    [MemberData(nameof(TypedIds))]
    public void A_typed_id_is_a_readonly_struct_built_from_one_guid(Type type)
    {
        type.IsValueType.ShouldBeTrue($"{type.Name} must be a struct");
        type.GetCustomAttributes().ShouldContain(
            attribute => attribute.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute",
            $"{type.Name} must be readonly");
        type.GetConstructor([typeof(Guid)]).ShouldNotBeNull($"{type.Name} must have a public constructor that takes the Guid");
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ShouldBe(["Value"]);
    }

    [Theory]
    [MemberData(nameof(TypedIds))]
    public void A_typed_id_round_trips_its_guid_and_prints_it(Type type)
    {
        var guid = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

        var id = (ITypedId)Activator.CreateInstance(type, guid)!;

        id.Value.ShouldBe(guid);
        id.ToString().ShouldBe("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    }

    [Theory]
    [MemberData(nameof(TypedIds))]
    public void The_default_of_a_typed_id_is_the_empty_guid(Type type)
    {
        var id = (ITypedId)Activator.CreateInstance(type)!;

        id.Value.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void Two_ids_of_the_same_type_are_equal_when_their_guids_are()
    {
        var guid = Guid.NewGuid();

        new TenantId(guid).ShouldBe(new TenantId(guid));
        (new TenantId(guid) == new TenantId(guid)).ShouldBeTrue();
        new TenantId(guid).ShouldNotBe(new TenantId(Guid.NewGuid()));
        new TenantId(guid).GetHashCode().ShouldBe(new TenantId(guid).GetHashCode());
    }

    [Fact]
    public void Ids_of_different_types_are_never_equal_even_with_the_same_guid()
    {
        var guid = Guid.NewGuid();

        new TenantId(guid).Equals(new UserId(guid)).ShouldBeFalse();
    }

    private static Type[] FindTypedIds()
        => [.. typeof(AssemblyMarker).Assembly.GetTypes().Where(type => typeof(ITypedId).IsAssignableFrom(type) && type != typeof(ITypedId))];
}
