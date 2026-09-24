using AdaptAula.Infrastructure.Ingestion;
using Xunit;

namespace AdaptAula.Tests;

public class QuestionSplitterTests
{
    [Fact]
    public void Split_DetectsNumberedQuestions_AndExtractsPointsHints()
    {
        var text = """
            1 · Reading & understanding · 3 points
            Read the paragraph and answer using complete sentences.

            2 · Classify · 2 points
            Classify these foods according to the nutrient they mainly provide.
            """;

        var (questions, confidence) = QuestionSplitter.Split(text);

        Assert.Equal(2, questions.Count);
        Assert.Equal(3, questions[0].Points);
        Assert.Equal(2, questions[1].Points);
        Assert.True(confidence > 0.6);
    }

    [Fact]
    public void Split_FallsBackToParagraphs_WhenNoNumberingIsPresent()
    {
        var text = "First unnumbered question about photosynthesis.\n\nSecond unnumbered question about the water cycle.";

        var (questions, confidence) = QuestionSplitter.Split(text);

        Assert.Equal(2, questions.Count);
        Assert.True(confidence < 0.6);
    }

    [Fact]
    public void Split_ReturnsLowConfidenceSingleBlob_WhenTextIsUnstructured()
    {
        var (questions, confidence) = QuestionSplitter.Split("Just one continuous blob of text with no structure at all.");

        Assert.Single(questions);
        Assert.True(confidence < 0.5);
    }
}
