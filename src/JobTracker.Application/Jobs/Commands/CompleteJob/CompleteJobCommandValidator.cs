using FluentValidation;

namespace JobTracker.Application.Jobs.Commands.CompleteJob;

public class CompleteJobCommandValidator : AbstractValidator<CompleteJobCommand>
{
    public CompleteJobCommandValidator()
    {
    }
}