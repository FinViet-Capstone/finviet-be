using FinViet.Application.Common;
using FinViet.Application.DTOs.Ai;

namespace FinViet.Infrastructure.ExternalServices.Gemini;

/// <summary>
/// Built-in prompt settings per feature, mirroring the V0010 seed rows — used only when the
/// ai_prompt_configs row is missing (e.g. a database restored from before that migration).
/// </summary>
internal static class AiPromptDefaults
{
    public const string AssistantPersona =
        "Bạn là trợ lý tài chính cá nhân của FinViet.\n" +
        "Luôn trả lời bằng tiếng Việt, giọng thân thiện, tích cực và hữu ích.";

    public const string ClassifierPersona =
        "Bạn là bộ phân loại giao dịch tài chính của FinViet.\n" +
        "Tuân thủ danh sách danh mục đóng và schema đầu ra.";

    public static AiPromptRuntimeConfig For(string featureKey) => featureKey switch
    {
        AiPromptFeatures.Chat => new AiPromptRuntimeConfig(AssistantPersona, 0.4, 768),
        AiPromptFeatures.WeeklyReport => new AiPromptRuntimeConfig(AssistantPersona, 0.5, 512),
        AiPromptFeatures.ScoreComment => new AiPromptRuntimeConfig(AssistantPersona, 0.5, 160),
        AiPromptFeatures.Classification => new AiPromptRuntimeConfig(ClassifierPersona, 0.1, 512),
        _ => new AiPromptRuntimeConfig(AssistantPersona, 0.4, 512)
    };
}
