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

    [Fact]
    public void Split_IgnoresEmbeddedNumberedLists_ThatDoNotContinueTheQuestionSequence()
    {
        // Regression test: a true/false table's row labels (1, 2, 3, 4) sit between question 22
        // and question 23 and must not be mistaken for new question boundaries, since they don't
        // continue the 19, 20, 21, 22, 23... sequence.
        var text = """
            19. What's the main idea of the text? Choose the right option among the four available answers provided below in this reading comprehension question.
            20. What do they have to do if they see a bear? Choose the right option among the four available answers provided below in this reading comprehension question.
            21. What is the setting for this text? Choose the right option among the four available answers provided below in this reading comprehension question.
            22. Are these statements true or false? Mark with an X in the appropriate column for each one.
            1. Susan is 13 years old
            2. Susan lives in England
            3. The bear's roar makes her scared
            4. Susan loves going hiking with adults
            23. What's another word for scary? Choose the right option among the four available answers provided below in this reading comprehension question.
            """;

        var (questions, confidence) = QuestionSplitter.Split(text);

        Assert.Equal(5, questions.Count);
        Assert.Contains("Are these statements true or false", questions[3].OriginalText);
        Assert.Contains("Susan is 13 years old", questions[3].OriginalText);
        Assert.Contains("What's another word for scary", questions[4].OriginalText);
        Assert.DoesNotContain("What's another word for scary", questions[3].OriginalText);
        Assert.True(confidence > 0.6);
    }
}
