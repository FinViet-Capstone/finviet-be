using System.ComponentModel.DataAnnotations;

namespace FinViet.Application.DTOs.Notifications;

public class RegisterNotificationDeviceRequest
{
    [Required]
    [MaxLength(255)]
    public string Token { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(ios|android)$")]
    public string Platform { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string InstallationId { get; set; } = string.Empty;
}

public class UnregisterNotificationDeviceRequest
{
    [Required]
    [MaxLength(100)]
    public string InstallationId { get; set; } = string.Empty;
}
