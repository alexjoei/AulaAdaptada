using System.Text.Json;
using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

public record MeasureGroupInfo(string Key, string Label);

/// <summary>
/// Loads the measure library — groups, atomic measures and the needs that bundle them — from
/// <c>Data/measure-library.json</c> (V2 §22). Nothing about "what a dyslexia profile gets" lives in C# code:
/// extending or editing the library is a data change, not a code change.
/// </summary>
public class MeasureLibrary
{
    private static readonly Lazy<MeasureLibrary> DefaultInstance = new(LoadEmbedded);
    public static MeasureLibrary Default => DefaultInstance.Value;

    public IReadOnlyList<MeasureGroupInfo> Groups { get; }
    public IReadOnlyList<AdaptationRule> Rules { get; }
    public IReadOnlyList<NecessityPreset> Needs { get; }
    public IReadOnlyDictionary<string, AdaptationRule> RulesById { get; }
    public IReadOnlyDictionary<string, NecessityPreset> NeedsByKey { get; }

    private MeasureLibrary(IReadOnlyList<MeasureGroupInfo> groups, IReadOnlyList<AdaptationRule> rules, IReadOnlyList<NecessityPreset> needs)
    {
        Groups = groups;
        Rules = rules;
        Needs = needs;
        RulesById = rules.ToDictionary(r => r.Id);
        NeedsByKey = needs.ToDictionary(n => n.Key);
    }

    public static MeasureLibrary Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<LibraryDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("measure-library.json está vacío.");

        var rules = dto.Measures.Select(ToRule).ToList();
        var ids = rules.Select(r => r.Id).ToHashSet();
        if (ids.Count != rules.Count)
            throw new InvalidOperationException("measure-library.json contiene ids de medida duplicados.");

        var needs = dto.Needs.Select(n =>
        {
            foreach (var measure in n.Measures)
                if (!ids.Contains(measure.RuleId))
                    throw new InvalidOperationException($"La necesidad '{n.Key}' referencia la medida inexistente '{measure.RuleId}'.");

            return new NecessityPreset
            {
                Key = n.Key,
                DisplayName = n.DisplayName,
                Description = n.Description,
                Hidden = n.Hidden,
                RuleIds = n.Measures.Select(m => m.RuleId).ToList(),
                Measures = n.Measures.Select(m => new NeedMeasure { RuleId = m.RuleId, Classification = ParseClassification(m.Classification) }).ToList()
            };
        }).ToList();

        return new MeasureLibrary(dto.Groups.Select(g => new MeasureGroupInfo(g.Key, g.Label)).ToList(), rules, needs);
    }

    private static MeasureLibrary LoadEmbedded()
    {
        using var stream = typeof(MeasureLibrary).Assembly.GetManifestResourceStream("measure-library.json")
            ?? throw new InvalidOperationException("No se encontró el recurso embebido measure-library.json.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static MeasureClassification ParseClassification(string value) => value.ToLowerInvariant() switch
    {
        "recommended" => MeasureClassification.Recommended,
        "optional" => MeasureClassification.Optional,
        "decision" => MeasureClassification.RequiresTeacherDecision,
        "not_recommended" => MeasureClassification.NotRecommended,
        _ => throw new InvalidOperationException($"Clasificación desconocida: {value}")
    };

    private static AdaptationRule ToRule(MeasureDto m) => new()
    {
        Id = m.Id,
        Description = m.Description,
        Group = Enum.Parse<MeasureGroup>(m.Group, true),
        Category = Enum.Parse<RuleCategory>(m.Category, true),
        Kind = Enum.Parse<MeasureKind>(m.Kind, true),
        ApplyMode = Enum.Parse<ApplyMode>(m.ApplyMode, true),
        MinLevel = m.MinLevel,
        RiskLevel = Enum.Parse<RiskLevel>(m.RiskLevel, true),
        BlockedByConstructTags = m.BlockedByConstructTags,
        RequiresTeacherReview = m.RequiresTeacherReview,
        AltersAssessedConstruct = m.AltersAssessedConstruct,
        Rationale = m.Rationale,
        DevNotes = m.DevNotes,
        Style = m.Style,
        ConflictsWith = m.ConflictsWith,
        Parameters = m.Parameters.Select(p => new MeasureParameter
        {
            Key = p.Key, Label = p.Label, Type = p.Type, Default = p.Default, Unit = p.Unit, Choices = p.Choices
        }).ToList()
    };

    private class LibraryDto
    {
        public List<GroupDto> Groups { get; set; } = new();
        public List<MeasureDto> Measures { get; set; } = new();
        public List<NeedDto> Needs { get; set; } = new();
    }

    private class GroupDto { public string Key { get; set; } = ""; public string Label { get; set; } = ""; }

    private class MeasureDto
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "Presentation";
        public string Category { get; set; } = "Structure";
        public string Kind { get; set; } = "Text";
        public string ApplyMode { get; set; } = "Default";
        public int MinLevel { get; set; } = 1;
        public string RiskLevel { get; set; } = "Low";
        public List<string> BlockedByConstructTags { get; set; } = new();
        public bool RequiresTeacherReview { get; set; }
        public bool AltersAssessedConstruct { get; set; }
        public string Rationale { get; set; } = "";
        public string DevNotes { get; set; } = "";
        public Dictionary<string, string> Style { get; set; } = new();
        public List<ParamDto> Parameters { get; set; } = new();
        public List<string> ConflictsWith { get; set; } = new();
    }

    private class ParamDto
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "number";
        public string Default { get; set; } = "";
        public string Unit { get; set; } = "";
        public List<string> Choices { get; set; } = new();
    }

    private class NeedDto
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Hidden { get; set; }
        public List<NeedMeasureDto> Measures { get; set; } = new();
    }

    private class NeedMeasureDto { public string RuleId { get; set; } = ""; public string Classification { get; set; } = "optional"; }
}
