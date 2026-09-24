using FluentValidation;

namespace FinViet.Application.Features.Ai.Commands.OverrideCategoryBatch;

public class OverrideCategoryBatchCommandValidator : AbstractValidator<OverrideCategoryBatchCommand>
{
    public const int MaxItems = 200;

    public OverrideCategoryBatchCommandValidator()
    {
        RuleFor(x => x.Request.Items)
            .NotEmpty().WithMessage("At least one item is required.")
            .Must(items => items.Count <= MaxItems)
            .WithMessage($"At most {MaxItems} items are allowed per request.")
            .Must(items => items.Select(i => i.TransactionId).Distinct().Count() == items.Count)
            .WithMessage("Each transaction may appear only once.");

        RuleForEach(x => x.Request.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.TransactionId).NotEmpty().WithMessage("Transaction id is required.");
            item.RuleFor(i => i.CategoryId).NotEmpty().WithMessage("Category id is required.");
        });
    }
}
