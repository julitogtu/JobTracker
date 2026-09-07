using FluentValidation;

namespace JobTracker.Application.Jobs.Commands.CompleteJob;

internal sealed class CompleteJobCommandValidator
    : AbstractValidator<CompleteJobCommand>
{
    public CompleteJobCommandValidator()
    {
        RuleFor(command => command.OrganizationId)
            .NotEmpty();

        RuleFor(command => command.JobId)
            .NotEmpty();

        RuleFor(command => command.SignatureUrl)
            .NotEmpty()
            .MaximumLength(2_048)
            .Must(BeValidAbsoluteUrl)
            .WithMessage(
                "Signature URL must be a valid absolute HTTP or HTTPS URL.");
    }

    private static bool BeValidAbsoluteUrl(string signatureUrl)
    {
        if (!Uri.TryCreate(
                signatureUrl,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}