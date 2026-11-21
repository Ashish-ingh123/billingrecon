using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="RequestAllocationReversalCommand"/>: the version, the allocation and a reason.</summary>
public sealed class RequestAllocationReversalCommandValidator : AbstractValidator<RequestAllocationReversalCommand>
{
    public RequestAllocationReversalCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.AllocationId).AllocationId();
        RuleFor(command => command.Reason).ReversalReason();
    }
}
