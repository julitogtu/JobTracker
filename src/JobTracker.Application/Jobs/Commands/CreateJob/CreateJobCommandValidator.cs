using FluentValidation;

namespace JobTracker.Application.Jobs.Commands.CreateJob;

internal sealed class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(command => command.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.Description)
            .NotEmpty()
            .MaximumLength(4_000);

        RuleFor(command => command.Street)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.City)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(command => command.State)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(command => command.ZipCode)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(command => command.Latitude)
            .InclusiveBetween(-90m, 90m);

        RuleFor(command => command.Longitude)
            .InclusiveBetween(-180m, 180m);

        RuleFor(command => command.CustomerId)
            .NotEmpty();

        RuleFor(command => command.OrganizationId)
            .NotEmpty();

        RuleFor(command => command.AssigneeId)
            .Must(assigneeId =>
                assigneeId is null ||
                assigneeId.Value != Guid.Empty)
            .WithMessage("Assignee ID cannot be empty.");

        RuleFor(command => command.ScheduledDateUtc)
            .Must(scheduledDate =>
                scheduledDate is null ||
                scheduledDate.Value > timeProvider.GetUtcNow())
            .WithMessage("Scheduled date must be in the future.");

        RuleFor(command => command)
            .Must(command =>
                command.ScheduledDateUtc.HasValue ==
                command.AssigneeId.HasValue)
            .WithMessage(
                "Scheduled date and assignee ID must be provided together.");
    }
}