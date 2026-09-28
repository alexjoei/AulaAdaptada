using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AdaptAula.Tests;

public class PdfExporterTests
{
    private static string TinyPngDataUri => $"data:image/png;base64,{Convert.ToBase64String(BuildTinyPng())}";

    private static byte[] BuildTinyPng()
    {
        using var image = new Image<Rgba32>(2, 2);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    // Matches the real runtime format: DocumentIngestionService.ResolvedImageMarker wraps "IMG:n"
    // in U+E000 (invisible Private Use Area) on both sides.
    private static string Marker(int index) => $"IMG:{index}";

    [Fact]
    public void Export_IncludesTheSectionStimulusAndPerQuestionImages_WithoutThrowing()
    {
        var (assessment, plan, questionsById, adapted, questionWithImageId) = BuildFixture();

        var withImages = new PdfExporter().Export(assessment, plan, questionsById, adapted);

        // A real assertion on layout isn't practical against raw PDF bytes, but a PDF that actually
        // embedded two extra images is measurably bigger than the same document with none — this
        // catches a regression back to "images are silently dropped" without needing a PDF parser.
        var withoutImages = ExportWithoutImages(assessment, plan, questionsById, adapted, questionWithImageId);
        Assert.True(withImages.Length > withoutImages.Length,
            $"Expected the export with embedded images ({withImages.Length} bytes) to be larger than the same export with none ({withoutImages.Length} bytes).");
    }

    private static byte[] ExportWithoutImages(
        Assessment assessment, AdaptationPlan plan, Dictionary<Guid, Question> questionsById,
        List<AdaptedQuestion> adapted, Guid questionWithImageId)
    {
        var strippedAssessment = new Assessment
        {
            Id = assessment.Id,
            Title = assessment.Title,
            Subject = assessment.Subject,
            Grade = assessment.Grade,
            TotalPoints = assessment.TotalPoints,
            Sections = assessment.Sections.Select(s => new Section
            {
                Id = s.Id,
                AssessmentId = s.AssessmentId,
                Title = s.Title,
                Order = s.Order,
                StimulusText = s.StimulusText is null ? null : "Look at this information.",
                AssetRefs = new List<string>(),
                Questions = s.Questions.Select(q => new Question
                {
                    Id = q.Id,
                    SectionId = q.SectionId,
                    Order = q.Order,
                    OriginalText = q.OriginalText,
                    Points = q.Points,
                    Options = q.Options,
                    AssetRefs = new List<string>()
                }).ToList()
            }).ToList()
        };
        var strippedQuestionsById = strippedAssessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);
        return new PdfExporter().Export(strippedAssessment, plan, strippedQuestionsById, adapted);
    }

    private static (Assessment Assessment, AdaptationPlan Plan, Dictionary<Guid, Question> QuestionsById, List<AdaptedQuestion> Adapted, Guid QuestionWithImageId)
        BuildFixture()
    {
        var sectionId = Guid.NewGuid();
        var q1 = new Question { Id = Guid.NewGuid(), SectionId = sectionId, Order = 0, OriginalText = "What day is it?", Points = 1 };
        var q2 = new Question
        {
            Id = Guid.NewGuid(), SectionId = sectionId, Order = 1, OriginalText = "Describe the diagram.", Points = 1,
            AssetRefs = new List<string> { TinyPngDataUri }
        };

        var section = new Section
        {
            Id = sectionId,
            Title = "Grupo 1",
            Order = 0,
            StimulusText = $"Look at this information.\n{Marker(0)}\nNow answer the questions below.",
            AssetRefs = new List<string> { TinyPngDataUri },
            Questions = new List<Question> { q1, q2 }
        };

        var assessment = new Assessment
        {
            Title = "Sample Test",
            Subject = "English",
            Grade = 6,
            TotalPoints = 2,
            Sections = new List<Section> { section }
        };
        section.AssessmentId = assessment.Id;

        var plan = new AdaptationPlan { AssessmentId = assessment.Id };
        var adapted = new List<AdaptedQuestion>
        {
            new() { PlanId = plan.Id, QuestionId = q1.Id, AdaptedText = q1.OriginalText, Points = q1.Points },
            new() { PlanId = plan.Id, QuestionId = q2.Id, AdaptedText = q2.OriginalText, Points = q2.Points }
        };

        var questionsById = assessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);
        return (assessment, plan, questionsById, adapted, q2.Id);
    }
}
