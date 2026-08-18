namespace FinViet.Application.DTOs.Announcements;

public class CreateAnnouncementRequest
{
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string TargetSegment { get; set; } = "all";
}
