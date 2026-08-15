namespace FinViet.Application.DTOs.CategoryCorrections;

public class CategoryCorrectionResponseDto
{
    public Guid LogId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TransactionId { get; set; }
    public Guid? AdminId { get; set; }
    public string? CorrectedCategoryId { get; set; }
    public string? OriginalAiGuess { get; set; }
    public DateTime? CreatedAt { get; set; }
}
