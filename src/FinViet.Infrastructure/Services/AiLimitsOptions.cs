namespace FinViet.Infrastructure.Services;

/// <summary>Binds the "AiLimits" section of appsettings.json.</summary>
public class AiLimitsOptions
{
    public const string SectionName = "AiLimits";

    public int PerUserPerDay { get; set; } = 100;

    public int PerUserPerMinute { get; set; } = 6;
}
