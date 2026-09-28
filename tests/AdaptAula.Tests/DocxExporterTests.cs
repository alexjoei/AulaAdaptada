using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using DocumentFormat.OpenXml.Packaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AdaptAula.Tests;

public class DocxExporterTests
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
    public void Export_EmbedsOneImagePartPerSectionAndQuestionImage()
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

        var bytes = new DocxExporter().Export(assessment, plan, questionsById, adapted);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var imageParts = document.MainDocumentPart!.ImageParts.ToList();

        // One from the section's shared stimulus, one from the question's own AssetRefs — both the
        // "reading passage" and "per-question" image paths land in the actual output file.
        Assert.Equal(2, imageParts.Count);

        var bodyText = document.MainDocumentPart.Document.Body!.InnerText;
        Assert.Contains("Enunciado / texto de referencia", bodyText);
        Assert.Contains("Look at this information.", bodyText);
    }
}
