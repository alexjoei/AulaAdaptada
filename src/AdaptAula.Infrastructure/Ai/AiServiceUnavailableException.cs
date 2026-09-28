namespace AdaptAula.Infrastructure.Ai;

/// <summary>Thrown when Gemini is still unavailable after retrying transient failures (503
/// UNAVAILABLE / 429 rate-limited) and trying the configured fallback model. A distinct type so the
/// API layer can tell "the AI service is temporarily overloaded — try again shortly" apart from a
/// real bug in the request, and respond to the teacher with a clear, actionable message instead of
/// leaking a raw exception/stack trace to the browser.</summary>
public class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
