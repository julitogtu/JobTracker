using FluentValidation;

namespace JobTracker.Application.Jobs.Commands.StartJob;

internal sealed class StartJobCommandValidator: AbstractValidator<StartJobCommand>
{
    public StartJobCommandValidator()
    {
        RuleFor(command => command.OrganizationId)
            .NotEmpty();

        RuleFor(command => command.JobId)
            .NotEmpty();
    }
}