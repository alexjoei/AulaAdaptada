using AdaptAula.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AdaptAula.Tests;

/// <summary>Shared fixture for the exporter tests: a two-question test whose shared passage and second question each hold one image.</summary>
internal static class ExportFixtures
{
    public static string TinyPngDataUri => $"data:image/png;base64,{Convert.ToBase64String(BuildTinyPng())}";

    private static byte[] BuildTinyPng()
    {
        using var image = new Image<Rgba32>(2, 2);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    // Matches the real runtime format: DocumentIngestionService.ResolvedImageMarker wraps "IMG:n" in U+E000 on both sides.
    public static string Marker(int index) => $"IMG:{index}";

    public static (Assessment Assessment, AdaptationPlan Plan, Dictionary<Guid, Question> QuestionsById, List<AdaptedQuestion> Adapted, Guid QuestionWithImageId)
        Build(bool withImages = true)
    {
        var sectionId = Guid.NewGuid();
        var q1 = new Question { Id = Guid.NewGuid(), SectionId = sectionId, Order = 0, OriginalText = "What day is it?", Points = 1, Type = QuestionType.ShortAnswer };
        var q2 = new Question
        {
            Id = Guid.NewGuid(), SectionId = sectionId, Order = 1, OriginalText = "Describe the diagram.", Points = 1, Type = QuestionType.OpenText,
            AssetRefs = withImages ? new List<string> { TinyPngDataUri } : new List<string>()
        };

        var section = new Section
        {
            Id = sectionId,
            Title = "Grupo 1",
            Order = 0,
            StimulusText = withImages ? $"Look at this information.\n{Marker(0)}\nNow answer the questions below." : "Look at this information.\nNow answer the questions below.",
            AssetRefs = withImages ? new List<string> { TinyPngDataUri } : new List<string>(),
            Questions = new List<Question> { q1, q2 }
        };

        var assessment = new Assessment { Title = "Sample Test", Subject = "English", Grade = 6, TotalPoints = 2, Sections = new List<Section> { section } };
        section.AssessmentId = assessment.Id;

        var plan = new AdaptationPlan { AssessmentId = assessment.Id };
        var adapted = new List<AdaptedQuestion>
        {
            new() { PlanId = plan.Id, QuestionId = q1.Id, AdaptedText = q1.OriginalText, Points = q1.Points },
            new() { PlanId = plan.Id, QuestionId = q2.Id, AdaptedText = q2.OriginalText, Points = q2.Points }
        };

        return (assessment, plan, assessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id), adapted, q2.Id);
    }
}
