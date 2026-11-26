using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The shape of an uploaded image (ADR 0059): not empty, within the size of its kind, a PNG, JPEG or WebP file by its
/// own bytes, at most 2000 pixels a side, and called by the client what its bytes say it is. An SVG fails the third
/// rule like any other file that is not one of the three.
/// </summary>
public sealed class SetLetterheadImageCommandValidator : AbstractValidator<SetLetterheadImageCommand>
{
    /// <summary>Creates the validator.</summary>
    public SetLetterheadImageCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Kind).IsInEnum().WithMessage("The image is the logo or the signature.");

        RuleFor(command => command.Content)
            .Must(content => content is { Length: > 0 })
            .WithMessage("Send the image file as the body of the request.")
            .Must((command, content) => !Enum.IsDefined(command.Kind) || content.Length <= TenantImage.MaxBytesOf(command.Kind))
            .WithMessage(command => $"The file is too large: at most {TenantImage.MaxBytesOf(command.Kind) / 1024} KB.")
            .Must(content => LetterheadImageInspector.TryRead(content, out _))
            .WithMessage("The file is not a PNG, JPEG or WebP image. An SVG is not accepted.")
            .Must(content => LetterheadImageInspector.TryRead(content, out var info) && info.Width <= TenantImage.MaxSidePixels && info.Height <= TenantImage.MaxSidePixels)
            .WithMessage($"The image is too large: at most {TenantImage.MaxSidePixels} pixels a side.")
            .Must((command, content) => DeclaredAs(command.DeclaredContentType, content))
            .WithMessage("The Content-Type of the request is not what the file is: send image/png, image/jpeg or image/webp, as the file is.");
    }

    private static bool DeclaredAs(string? declared, byte[] content)
        => LetterheadImageInspector.TryRead(content, out var info)
            && string.Equals(declared?.Trim(), info.ContentType, StringComparison.OrdinalIgnoreCase);
}
