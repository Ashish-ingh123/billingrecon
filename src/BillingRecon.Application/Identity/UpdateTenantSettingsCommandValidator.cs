using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of new settings: a threshold above zero or none, and two counts of days of 0 to 365 (BR-205, BR-207).</summary>
public sealed class UpdateTenantSettingsCommandValidator : AbstractValidator<UpdateTenantSettingsCommand>
{
    /// <summary>Creates the validator.</summary>
    public UpdateTenantSettingsCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.PostingApprovalThreshold).Threshold();
        RuleFor(command => command.MaxBackdateDays).Days("back-date window");
        RuleFor(command => command.DefaultDueDays).Days("number of default due days");
    }
}
