using System.Runtime.CompilerServices;

// Lets tests exercise GeminiHttpExecutor's retry/fallback logic directly (with injectable, near-zero
// delays) instead of only through the public Gemini classes, which would otherwise force tests to
// wait out the real production backoff.
[assembly: InternalsVisibleTo("AdaptAula.Tests")]
