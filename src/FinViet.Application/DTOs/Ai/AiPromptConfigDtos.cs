namespace FinViet.Application.DTOs.Ai;

/// <summary>Admin-facing view of one feature's editable prompt settings.</summary>
public record AiPromptConfigDto(
    string FeatureKey,
    string DisplayName,
    string PersonaInstruction,
    decimal Temperature,
    int MaxOutputTokens,
    Guid? UpdatedBy,
    string? UpdatedByUsername,
    DateTime UpdatedAt);

/// <summary>One snapshot in a feature's prompt-config change timeline (newest first).</summary>
public record AiPromptConfigHistoryDto(
    Guid Id,
    string FeatureKey,
    string PersonaInstruction,
    decimal Temperature,
    int MaxOutputTokens,
    Guid? ChangedBy,
    string? ChangedByUsername,
    DateTime ChangedAt);

/// <summary>Request body for PUT /api/ai/prompt-configs/{featureKey}.</summary>
public record UpdateAiPromptConfigRequest(
    string PersonaInstruction,
    decimal Temperature,
    int MaxOutputTokens);

/// <summary>
/// The values the Gemini client consumes at generation time. The persona is the admin-editable
/// part only — the fixed safety core is prepended by the client itself, never stored or edited.
/// </summary>
public record AiPromptRuntimeConfig(
    string PersonaInstruction,
    double Temperature,
    int MaxOutputTokens);
