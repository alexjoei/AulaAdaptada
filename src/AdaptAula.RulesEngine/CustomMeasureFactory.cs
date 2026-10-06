using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

/// <summary>Turns a teacher-defined <see cref="CustomMeasure"/> into an <see cref="AdaptationRule"/> the resolver treats like any library measure.</summary>
public static class CustomMeasureFactory
{
    public const string IdPrefix = "custom.";

    public static AdaptationRule ToRule(CustomMeasure custom) => new()
    {
        Id = custom.Id,
        Description = custom.Description,
        Group = custom.Group,
        Category = RuleCategory.Support,
        Kind = custom.Kind,
        ApplyMode = ApplyMode.Configurable,
        MinLevel = 1,
        RiskLevel = custom.AltersAssessedConstruct ? RiskLevel.High : RiskLevel.Medium,
        RequiresTeacherReview = true,
        AltersAssessedConstruct = custom.AltersAssessedConstruct,
        Rationale = "Medida definida por el docente.",
        IsCustom = true
    };

    public static string NewId() => IdPrefix + Guid.NewGuid().ToString("N")[..8];
}
