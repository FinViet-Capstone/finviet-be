namespace FinViet.Application.DTOs.Admins;

public record AdminResponse(Guid AdminId, string Username, string Email, DateTime CreatedAt);
