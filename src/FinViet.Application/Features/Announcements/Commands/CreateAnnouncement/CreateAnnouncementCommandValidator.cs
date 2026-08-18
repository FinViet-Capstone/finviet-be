using FluentValidation;

namespace FinViet.Application.Features.Announcements.Commands.CreateAnnouncement;

public class CreateAnnouncementCommandValidator : AbstractValidator<CreateAnnouncementCommand>
{
    // Only "all" is supported until segment-targeting criteria (plan? recent activity?) is decided.
    private static readonly string[] SupportedTargetSegments = { "all" };

    public CreateAnnouncementCommandValidator()
    {
        RuleFor(x => x.Request.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must be at most 200 characters.");

        RuleFor(x => x.Request.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MaximumLength(500).WithMessage("Message must be at most 500 characters.");

        RuleFor(x => x.Request.TargetSegment)
            .NotEmpty().WithMessage("targetSegment is required.")
            .Must(segment => SupportedTargetSegments.Contains(segment))
            .WithMessage("targetSegment must be one of: all");
    }
}
