using AdaptAula.Domain;
using AdaptAula.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdaptAula.Tests;

/// <summary>
/// A real bug shipped and was only caught by driving an actual browser: images and passage content
/// visibly present right after upload were missing once the Analysis screen re-fetched the same
/// assessment. No test exercising DocumentIngestionService in isolation — including the ones already
/// covering marker interleaving — could have caught it, because nothing in that suite ever persists
/// and reloads the result the way the real app does on every page load. This exercises that exact
/// round trip: save through AdaptAulaDbContext against a real SQLite connection, then read it back
/// through a brand new context (no in-process change-tracker cache to mask a real storage problem).
/// </summary>
public class DocumentPersistenceTests
{
    private static AdaptAulaDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AdaptAulaDbContext>().UseSqlite(connection).Options;
        var context = new AdaptAulaDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void SectionStimulusText_WithEmbeddedImageMarkers_SurvivesASaveAndReloadRoundTrip()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open(); // keeps the in-memory DB alive for the life of the connection, across contexts

        var assessmentId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        // The exact marker shape DocumentIngestionService.ResolvedImageMarker produces.
        var fullText = "Before the marker.IMG:0After the marker, which must survive the round trip.";

        using (var write = CreateContext(connection))
        {
            var assessment = new Assessment { Id = assessmentId, Title = "Test" };
            var section = new Section { Id = sectionId, AssessmentId = assessmentId, StimulusText = fullText };
            assessment.Sections.Add(section);
            write.Assessments.Add(assessment);
            write.SaveChanges();
        }

        using var read = CreateContext(connection);
        var reloaded = read.Sections.Single(s => s.Id == sectionId);
        Assert.Equal(fullText, reloaded.StimulusText);
    }
}
