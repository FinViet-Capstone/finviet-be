namespace FinViet.Application.Exceptions;

/// <summary>Thrown when the Gemini API is unreachable, times out, returns an error status,
/// or returns a response that cannot be parsed. Callers should treat this as "AI unavailable"
/// and apply their fallback (e.g. mark transaction Uncategorized + enqueue for re-processing).</summary>
public class GeminiUnavailableException : Exception
{
    public GeminiUnavailableException(string message) : base(message)
    {
    }

    public GeminiUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
