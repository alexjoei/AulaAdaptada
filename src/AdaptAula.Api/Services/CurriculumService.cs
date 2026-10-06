using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ai;
using AdaptAula.RulesEngine;

namespace AdaptAula.Api.Services;

public record CurriculumCandidates(string AreaName, List<AnalysisCandidate> Criteria, List<AnalysisCandidate> Contents);

/// <summary>Read-only access to the structured curriculum (V2 §6) and the helpers that cross it with a test — never with a need.</summary>
public class CurriculumService
{
    public IReadOnlyList<Curriculum> All => CurriculumCatalog.Default.Curricula;

    public Curriculum? Get(string? id) => CurriculumCatalog.Default.Get(id);

    /// <summary>Plain-language lines for the criteria/contents the teacher picked, handed to the AI as the referents of a curricular adaptation.</summary>
    public List<string> DescribeReferents(IEnumerable<string> criteriaIds, IEnumerable<string> contentIds)
    {
        var criteriaSet = criteriaIds.ToHashSet();
        var contentSet = contentIds.ToHashSet();
        var lines = new List<string>();
        foreach (var curriculum in All)
            foreach (var area in curriculum.Areas)
            {
                lines.AddRange(area.Criteria.Where(c => criteriaSet.Contains(c.Id)).Select(c => $"Criterio {c.Id} ({area.Name}): {c.Text}"));
                lines.AddRange(area.Contents.Where(c => contentSet.Contains(c.Id)).Select(c => $"Saber {c.Id} ({area.Name}): {c.Text}"));
            }
        return lines;
    }

    /// <summary>Criteria and contents of the assessment's area for its grade's cycle — the only ids the analyzer may choose from.</summary>
    public CurriculumCandidates? CandidatesFor(Assessment assessment)
    {
        var curriculum = Get(assessment.CurriculumId);
        var area = curriculum?.Areas.FirstOrDefault(a => a.Id == assessment.CurriculumAreaId);
        if (curriculum is null || area is null) return null;

        var cycle = assessment.Grade is { } grade ? curriculum.CycleForGrade(grade) : null;
        var criteria = area.Criteria.Where(c => cycle is null || c.Cycle == cycle).Select(c => new AnalysisCandidate(c.Id, c.Text)).ToList();
        var contents = area.Contents.Where(c => cycle is null || c.Cycle == cycle).Select(c => new AnalysisCandidate(c.Id, $"{c.Block}: {c.Text}")).ToList();
        return new CurriculumCandidates(area.Name, criteria, contents);
    }

    public Dictionary<string, string> CriteriaTexts() =>
        All.SelectMany(c => c.Areas).SelectMany(a => a.Criteria).ToDictionary(c => c.Id, c => c.Text);
}
