using BillingRecon.Api.Audit;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Domain.Common;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Who reads the audit trail of a record is whoever may read the record (review 2026-11, finding R7): every kind of
/// record that can be audited is named in <see cref="AuditTrailPolicies"/> with a policy of the catalogue, so a new
/// aggregate has no reader of its trail until someone decides who that is.
/// </summary>
public sealed class AuditTrailPolicyTests
{
    // A record is audited by the events of its aggregate (ADR 0020): the aggregates are the kinds a trail can be of.
    private static readonly string[] AuditedKinds =
    [
        .. Assemblies.Domain.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && IsAggregate(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal),
    ];

    [Fact]
    public void Every_kind_of_audited_record_names_the_policy_that_reads_its_trail_and_nothing_else_is_named()
    {
        AuditedKinds.ShouldNotBeEmpty();

        AuditTrailPolicies.ByEntityType.Keys.Order(StringComparer.Ordinal).ToArray().ShouldBe(
            AuditedKinds,
            customMessage: "a new aggregate needs its line in AuditTrailPolicies: until then nobody reads its trail");
    }

    [Fact]
    public void Every_policy_named_for_a_trail_is_a_policy_of_the_catalogue()
    {
        var catalogue = typeof(Policies).GetFields()
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (kind, policy) in AuditTrailPolicies.ByEntityType)
        {
            catalogue.ShouldContain(policy, $"the trail of {kind}");
            policy.ShouldNotBe(Policies.ReadAudit, $"the trail of {kind} is read by who reads {kind}, not by everyone who reads some trail");
            policy.ShouldNotBe(Policies.SignedIn, $"the trail of {kind}");
        }
    }

    [Fact]
    public void A_kind_is_found_whatever_its_case_as_the_database_compares_it()
    {
        AuditTrailPolicies.ByEntityType.ContainsKey("adjustment").ShouldBeTrue();
        AuditTrailPolicies.ByEntityType.ContainsKey("ADJUSTMENT").ShouldBeTrue();
        AuditTrailPolicies.ByEntityType.ContainsKey("Widget").ShouldBeFalse();
    }

    private static bool IsAggregate(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }
}
