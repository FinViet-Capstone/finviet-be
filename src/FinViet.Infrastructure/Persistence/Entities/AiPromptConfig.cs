using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiPromptConfig
{
    public string FeatureKey { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string PersonaInstruction { get; set; } = null!;

    public decimal Temperature { get; set; }

    public int MaxOutputTokens { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }
}
