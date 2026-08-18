namespace FinViet.Application.DTOs.Announcements;

public class AnnouncementResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string TargetLabel { get; set; } = null!;
    public int RecipientCount { get; set; }
    public DateTime SentAt { get; set; }
}
