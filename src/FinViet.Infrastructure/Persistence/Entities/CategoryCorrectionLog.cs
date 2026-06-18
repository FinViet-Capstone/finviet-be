using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class CategoryCorrectionLog
{
    public Guid LogId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? TransactionId { get; set; }

    public Guid? AdminId { get; set; }

    public Guid? CorrectedCategoryId { get; set; }

    public string? OriginalAiGuess { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Admin? Admin { get; set; }

    public virtual Category? CorrectedCategory { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Transaction? Transaction { get; set; }
}
