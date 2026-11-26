using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Reporting;
using BillingRecon.Domain.Common;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Who is shown a waiting request by the pending-approvals report is whoever may read its document (review 2026-11,
/// finding R8): every kind of document a request can be for is named in <see cref="PendingApprovalPolicies"/> with a
/// policy of the catalogue, so a new kind is shown to no one but the deciders until someone decides who reads it.
/// </summary>
public sealed class PendingApprovalPolicyTests
{
    [Fact]
    public void Every_kind_of_document_a_request_can_be_for_names_the_policy_that_reads_it()
        => PendingApprovalPolicies.ByDocumentType.Keys.Order().ToArray().ShouldBe(
            Enum.GetValues<ApprovalDocumentType>().Order().ToArray(),
            customMessage: "a new kind of approval request needs its line in PendingApprovalPolicies");

    [Fact]
    public void Every_policy_named_is_a_read_policy_of_the_catalogue_or_the_deciders()
    {
        var catalogue = typeof(Policies).GetFields()
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (kind, policy) in PendingApprovalPolicies.ByDocumentType)
        {
            catalogue.ShouldContain(policy, $"the requests of {kind}");
            policy.ShouldNotBe(Policies.Reports, $"the requests of {kind} are shown to who reads {kind}, not to everyone who reads some report");
            policy.ShouldNotBe(Policies.Dashboard, $"the requests of {kind}");
            policy.ShouldNotBe(Policies.SignedIn, $"the requests of {kind}");
        }
    }
}
