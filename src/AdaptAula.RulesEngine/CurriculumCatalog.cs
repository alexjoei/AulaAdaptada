using System.Reflection;
using System.Text.Json;

namespace AdaptAula.RulesEngine;

public class CurriculumCycle
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<int> Grades { get; set; } = new();
}

public class KeyDescriptor
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public class KeyCompetence
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<KeyDescriptor> Descriptors { get; set; } = new();
}

public class SpecificCompetence
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string Text { get; set; } = "";
    public List<string> KeyDescriptors { get; set; } = new();
}

public class EvaluationCriterion
{
    public string Id { get; set; } = "";
    public string Cycle { get; set; } = "";
    public string CompetenceId { get; set; } = "";
    public string Text { get; set; } = "";
}

public class BasicContent
{
    public string Id { get; set; } = "";
    public string Cycle { get; set; } = "";
    public string Block { get; set; } = "";
    public string Text { get; set; } = "";
}

public class CurriculumArea
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<SpecificCompetence> Competences { get; set; } = new();
    public List<EvaluationCriterion> Criteria { get; set; } = new();
    public List<BasicContent> Contents { get; set; } = new();
}

/// <summary>
/// One curriculum (V2 §6): CCAA → stage → cycle → area → specific competences → evaluation criteria → basic contents →
/// key competences/descriptors. It is a layer of its own: nothing in it refers to a need or diagnosis, so the curriculum is
/// never modified because of a diagnosis (V2 §13) — the engine only crosses it with the educational measures.
/// </summary>
public class Curriculum
{
    public string Id { get; set; } = "";
    public string Ccaa { get; set; } = "";
    public string Stage { get; set; } = "";
    public string LegalBasis { get; set; } = "";

    /// <summary>False while the transcription has not been checked against the official text.</summary>
    public bool Verified { get; set; }
    public string Note { get; set; } = "";
    public List<CurriculumCycle> Cycles { get; set; } = new();
    public List<KeyCompetence> KeyCompetences { get; set; } = new();
    public List<CurriculumArea> Areas { get; set; } = new();

    public string? CycleForGrade(int grade) => Cycles.FirstOrDefault(c => c.Grades.Contains(grade))?.Id;
}

/// <summary>Curricula are data files under <c>Data/curriculum/</c>; adding another CCAA or stage is dropping in a JSON file.</summary>
public class CurriculumCatalog
{
    private static readonly Lazy<CurriculumCatalog> DefaultInstance = new(LoadEmbedded);
    public static CurriculumCatalog Default => DefaultInstance.Value;

    public IReadOnlyList<Curriculum> Curricula { get; }

    public CurriculumCatalog(IReadOnlyList<Curriculum> curricula) => Curricula = curricula;

    public Curriculum? Get(string? id) => Curricula.FirstOrDefault(c => c.Id == id);

    private static CurriculumCatalog LoadEmbedded()
    {
        var assembly = typeof(CurriculumCatalog).Assembly;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var curricula = new List<Curriculum>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("curriculum/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var curriculum = JsonSerializer.Deserialize<Curriculum>(stream, options);
            if (curriculum is not null) curricula.Add(curriculum);
        }
        return new CurriculumCatalog(curricula);
    }
}
