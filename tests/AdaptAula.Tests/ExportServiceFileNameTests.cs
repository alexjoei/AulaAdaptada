using AdaptAula.Api.Services;
using AdaptAula.Domain;
using Xunit;

namespace AdaptAula.Tests;

public class ExportServiceFileNameTests
{
    private static Assessment BuildAssessment(int? grade, string? subject, string language = "es", string? sourceFileName = "Original Test File.pdf") =>
        new() { Grade = grade, Subject = subject, Language = language, SourceFileName = sourceFileName, Title = "Fallback Title" };

    [Fact]
    public void BuildFileName_CombinesGradeSubjectLanguageAndOriginalName_WhenAllArePresent()
    {
        var assessment = BuildAssessment(grade: 6, subject: "English");

        var fileName = ExportService.BuildFileName(assessment, profile: null, extension: "pdf");

        Assert.Equal("6-English-es-Original_Test_File.pdf", fileName);
    }

    [Fact]
    public void BuildFileName_SkipsGradeAndSubject_WhenNeitherWasSpecified()
    {
        var assessment = BuildAssessment(grade: null, subject: null);

        var fileName = ExportService.BuildFileName(assessment, profile: null, extension: "pdf");

        Assert.Equal("es-Original_Test_File.pdf", fileName);
    }

    [Fact]
    public void BuildFileName_IncludesAccommodationLabels_FromTheProfilesMeasures()
    {
        var assessment = BuildAssessment(grade: 4, subject: "Math");
        var profile = new StudentProfile { Measures = new List<string> { "dyslexia", "adhd" } };

        var fileName = ExportService.BuildFileName(assessment, profile, extension: "docx");

        Assert.Equal("4-Math-es-Dislexia+TDAH-Original_Test_File.docx", fileName);
    }

    [Fact]
    public void BuildFileName_FallsBackToTheAssessmentTitle_WhenThereIsNoSourceFileName()
    {
        var assessment = BuildAssessment(grade: 3, subject: "Science", sourceFileName: null);

        var fileName = ExportService.BuildFileName(assessment, profile: null, extension: "pdf");

        Assert.Equal("3-Science-es-Fallback_Title.pdf", fileName);
    }

    [Fact]
    public void BuildFileName_NeverProducesCharactersUnsafeForAFileSystem()
    {
        var assessment = BuildAssessment(grade: 6, subject: "Lengua/Literatura: 2º?", sourceFileName: "weird:name*.pdf");

        var fileName = ExportService.BuildFileName(assessment, profile: null, extension: "pdf");

        Assert.DoesNotContain('/', fileName);
        Assert.DoesNotContain(':', fileName);
        Assert.DoesNotContain('*', fileName);
        Assert.DoesNotContain('?', fileName);
    }
}
