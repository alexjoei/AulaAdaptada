using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

/// <summary>
/// Named bundles of atomic rule ids, one per "necesidad" row group in the rules matrix.
/// Presets are editable data (a teacher/admin can add/remove rule ids from a preset later);
/// selecting a preset is never more than "start from this set of atomic rules".
/// MVP1 surfaces dyslexia, adhd, tdl_tel, asd, curricular and gifted in the UI; the remaining
/// presets are seeded now so later MVPs (hearing/vision/motor/etc., per the roadmap) are pure
/// UI work, not rules-engine work.
/// </summary>
public static class NecessityPresets
{
    public static readonly IReadOnlyList<NecessityPreset> All = new List<NecessityPreset>
    {
        Preset("adhd", "TDAH", "adhd.fragment_tasks", "adhd.one_action_per_block",
            "adhd.highlight_action_verbs", "adhd.progress_checklist", "adhd.configurable_time"),

        Preset("dyslexia", "Dislexia", "dyslexia.typography_sans_serif", "dyslexia.left_align_low_density",
            "dyslexia.short_statements_keywords", "dyslexia.read_aloud_audio"),

        Preset("dysgraphia", "Disgrafía", "dysgraphia.reduce_copying", "dysgraphia.alt_response_mode"),

        Preset("dysorthography", "Disortografía", "dysorthography.separate_content_spelling",
            "dysorthography.word_bank_corrector"),

        Preset("dyscalculia", "Discalculia", "dyscalculia.align_figures_grid",
            "dyscalculia.data_operation_answer_split", "dyscalculia.reference_material"),

        Preset("tdl_tel", "TDL/TEL", "tdl_tel.direct_syntax", "tdl_tel.glossary_visual_support",
            "tdl_tel.split_multi_instructions"),

        Preset("asd", "TEA", "asd.predictable_stable_format", "asd.literal_explicit_language",
            "asd.task_step_count"),

        Preset("hearing_impairment", "Hipoacusia", "hearing.written_backup_for_oral",
            "hearing.subtitles_transcript"),

        Preset("low_vision", "Baja visión", "low_vision.configurable_size_contrast",
            "low_vision.no_color_only_coding", "low_vision.alt_text_screen_reader"),

        Preset("motor_disability", "Discapacidad motora", "motor.minimize_nonessential_writing",
            "motor.keyboard_selection_voice"),

        Preset("curricular", "Adaptación curricular", "curricular.never_infer_from_diagnosis",
            "curricular.use_teacher_defined_referents_only"),

        Preset("reading_comprehension", "Comprensión lectora", "reading_comprehension.fragment_text_near_questions",
            "reading_comprehension.anticipate_vocabulary"),

        Preset("slow_processing", "Procesamiento lento", "slow_processing.lower_density_per_page",
            "slow_processing.configurable_time_pauses"),

        Preset("executive_functions", "Funciones ejecutivas", "executive_functions.checklist_steps_progress"),

        Preset("spanish_l2", "Castellano L2", "spanish_l2.visual_glossary_clear_language",
            "spanish_l2.translation_only_if_authorized"),

        Preset("gifted", "Altas capacidades", "gifted.optional_depth_extension"),
    };

    public static readonly IReadOnlyDictionary<string, NecessityPreset> ByKey =
        All.ToDictionary(p => p.Key, p => p);

    /// <summary>Presets exposed in the MVP1 profile selector, per the approved plan's scope cut.</summary>
    public static readonly IReadOnlyList<string> Mvp1Keys = new List<string>
    {
        "dyslexia", "adhd", "tdl_tel", "asd", "curricular", "gifted"
    };

    private static NecessityPreset Preset(string key, string displayName, params string[] ruleIds) => new()
    {
        Key = key,
        DisplayName = displayName,
        RuleIds = ruleIds.ToList()
    };
}
