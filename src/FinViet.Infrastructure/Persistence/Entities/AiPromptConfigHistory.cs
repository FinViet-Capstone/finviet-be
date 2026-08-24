using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiPromptConfigHistory
{
    public Guid Id { get; set; }

    public string FeatureKey { get; set; } = null!;

    public string PersonaInstruction { get; set; } = null!;

    public decimal Temperature { get; set; }

    public int MaxOutputTokens { get; set; }

    public Guid? ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; }
}
