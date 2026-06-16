namespace FinViet.Application.DTOs.Ai;

public class CategorizePreviewRequest
{
    /// <summary>Free-text beneficiary name or note to classify.</summary>
    public string Input { get; set; } = null!;
}
