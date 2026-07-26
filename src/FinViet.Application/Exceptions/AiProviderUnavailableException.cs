namespace FinViet.Application.Exceptions;

/// <summary>Thrown when the configured AI provider is unreachable, times out, returns an error
/// status, or returns a response that cannot be parsed. Callers should apply their normal
/// AI-unavailable fallback.</summary>
public class AiProviderUnavailableException : Exception
{
    public AiProviderUnavailableException(string message) : base(message)
    {
    }

    public AiProviderUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
