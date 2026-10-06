namespace AdaptAula.Infrastructure.Export;

internal static class TextSegments
{
    /// <summary>Splits text into alternating runs of spaces and non-spaces, so word spacing can be applied to the spaces only.</summary>
    public static List<(string Text, bool IsSpace)> SplitSpaces(string text)
    {
        var result = new List<(string, bool)>();
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || (text[i] == ' ') != (text[start] == ' '))
            {
                result.Add((text[start..i], text[start] == ' '));
                start = i;
            }
        }
        if (text.Length == 0) result.Add((string.Empty, false));
        return result;
    }
}
