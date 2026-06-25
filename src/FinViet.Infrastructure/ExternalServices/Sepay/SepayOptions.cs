namespace FinViet.Infrastructure.ExternalServices.Sepay;

/// <summary>Binds the "Sepay" section of appsettings.json.</summary>
public class SepayOptions
{
    public const string SectionName = "Sepay";

    public string BaseUrl { get; set; } = "https://userapi.sepay.vn";

    public int TimeoutSeconds { get; set; } = 30;
}
