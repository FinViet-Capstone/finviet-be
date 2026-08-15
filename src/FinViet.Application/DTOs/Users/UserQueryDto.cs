namespace FinViet.Application.DTOs.Users;

public class UserQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Case-insensitive substring match against email or full name.</summary>
    public string? Search { get; set; }
}
