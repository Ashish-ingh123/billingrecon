using BillingRecon.Domain.Billing;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of <see cref="CreateServiceItemCommand"/>: a code the value object accepts, and a valid name, category,
/// price and tax rate. That the code is free in the tenant is a question for the database, answered by the handler.
/// </summary>
public sealed class CreateServiceItemCommandValidator : AbstractValidator<CreateServiceItemCommand>
{
    public CreateServiceItemCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Code)
            .Must(code => ServiceItemCode.TryOf(code, out _))
            .WithMessage($"The code is required: 1 to {ServiceItemCode.MaxLength} characters, each a letter, a digit or one of . - _ /");

        Include(new ServiceItemDetailsValidator());
    }
}
