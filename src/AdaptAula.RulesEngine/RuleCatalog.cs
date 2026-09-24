using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

/// <summary>
/// The atomic rule catalog, transcribed verbatim from AdaptAula_Matriz_Reglas.pdf (39 rows).
/// This is the single source of truth for "what an adaptation can do" — necessity presets
/// (<see cref="NecessityPresets"/>) are just named bundles of these ids. Nothing here is
/// branched on a profile/diagnosis in code; the catalog is plain data.
/// </summary>
public static class RuleCatalog
{
    public static readonly IReadOnlyList<AdaptationRule> All = new List<AdaptationRule>
    {
        // TDAH
        Rule("adhd.fragment_tasks", "Fragmentar tareas largas en bloques breves",
            RuleCategory.Structure, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("adhd.one_action_per_block", "Máximo una acción principal por bloque; numerar pasos",
            RuleCategory.Instructions, ApplyMode.Default, 1, RiskLevel.Low,
            devNotes: "instruction.max_actions=1"),
        Rule("adhd.highlight_action_verbs", "Destacar verbos de acción y datos esenciales",
            RuleCategory.Attention, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("adhd.progress_checklist", "Añadir checklist de progreso/revisión",
            RuleCategory.SelfRegulation, ApplyMode.Default, 2, RiskLevel.Low),
        Rule("adhd.configurable_time", "Permitir tiempo/pausas configurables",
            RuleCategory.Time, ApplyMode.Configurable, 2, RiskLevel.Medium,
            requiresReview: true, devNotes: "Valor definido por docente"),

        // Dislexia
        Rule("dyslexia.typography_sans_serif", "Sans serif clara, tamaño 12-14+ configurable",
            RuleCategory.Typography, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("dyslexia.left_align_low_density", "Alineación izquierda; evitar justificado y bloques densos",
            RuleCategory.Layout, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("dyslexia.short_statements_keywords", "Enunciados breves y separados; destacar palabras clave",
            RuleCategory.Language, ApplyMode.Default, 1, RiskLevel.Low,
            blockedBy: new() { ConstructTags.Reading }, requiresReview: true,
            devNotes: "No aplicar si simplifica el constructo de lectura"),
        Rule("dyslexia.read_aloud_audio", "Lectura en voz alta/audio cuando esté permitido",
            RuleCategory.Access, ApplyMode.Configurable, 2, RiskLevel.High,
            blockedBy: new() { ConstructTags.Reading }, requiresReview: true,
            devNotes: "No aplicar si lectura es el constructo"),

        // Disgrafía
        Rule("dysgraphia.reduce_copying", "Reducir copia innecesaria; ampliar espacios",
            RuleCategory.Response, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("dysgraphia.alt_response_mode", "Permitir teclado/selección/oral según medidas",
            RuleCategory.Response, ApplyMode.Configurable, 2, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.Writing }, requiresReview: true,
            devNotes: "No aplicar si escritura manual es el constructo"),

        // Disortografía
        Rule("dysorthography.separate_content_spelling", "Separar contenido y ortografía",
            RuleCategory.Evaluation, ApplyMode.Default, 1, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.Spelling }, requiresReview: true,
            devNotes: "No aplicar si ortografía es el constructo"),
        Rule("dysorthography.word_bank_corrector", "Banco de palabras/corrector solo autorizado",
            RuleCategory.Support, ApplyMode.Configurable, 2, RiskLevel.High,
            blockedBy: new() { ConstructTags.Spelling }, requiresReview: true,
            devNotes: "No aplicar si ortografía es el constructo. Validator: SPELLING_CONSTRUCT_BYPASSED"),

        // Discalculia
        Rule("dyscalculia.align_figures_grid", "Alinear cifras y usar cuadrícula cuando ayude",
            RuleCategory.Layout, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("dyscalculia.data_operation_answer_split", "Separar Datos / Operación / Respuesta",
            RuleCategory.Problems, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("dyscalculia.reference_material", "Material de referencia/manipulativo solo autorizado",
            RuleCategory.Support, ApplyMode.Configurable, 2, RiskLevel.High,
            blockedBy: new() { ConstructTags.Calculation, ConstructTags.Memory }, requiresReview: true,
            devNotes: "No aplicar si cálculo/memoria es el constructo"),

        // TDL/TEL
        Rule("tdl_tel.direct_syntax", "Sintaxis directa sin eliminar vocabulario curricular clave",
            RuleCategory.Language, ApplyMode.Default, 1, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.LinguisticComplexity }, requiresReview: true,
            devNotes: "No aplicar si complejidad lingüística es el constructo"),
        Rule("tdl_tel.glossary_visual_support", "Glosario breve y apoyo visual",
            RuleCategory.Support, ApplyMode.Default, 2, RiskLevel.Medium,
            requiresReview: true, devNotes: "No revelar respuestas. Validator: HINT_REVEALS_ANSWER"),
        Rule("tdl_tel.split_multi_instructions", "Dividir instrucciones múltiples",
            RuleCategory.Instructions, ApplyMode.Default, 1, RiskLevel.Low),

        // TEA
        Rule("asd.predictable_stable_format", "Formato predecible y estable",
            RuleCategory.Structure, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("asd.literal_explicit_language", "Lenguaje literal y explícito; evitar ambigüedad innecesaria",
            RuleCategory.Language, ApplyMode.Default, 1, RiskLevel.Low,
            blockedBy: new() { ConstructTags.FigurativeLanguage }, requiresReview: true,
            devNotes: "No aplicar si lenguaje figurado/ambigüedad es el constructo"),
        Rule("asd.task_step_count", "Indicar número de tareas y pasos",
            RuleCategory.Orientation, ApplyMode.Default, 1, RiskLevel.Low),

        // Hipoacusia
        Rule("hearing.written_backup_for_oral", "Toda instrucción oral relevante también por escrito",
            RuleCategory.Access, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("hearing.subtitles_transcript", "Subtítulos/transcripción cuando proceda",
            RuleCategory.Multimedia, ApplyMode.Default, 1, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.ListeningComprehension }, requiresReview: true,
            devNotes: "No aplicar si comprensión auditiva es el constructo y la medida no lo permite"),

        // Baja visión
        Rule("low_vision.configurable_size_contrast", "Tamaño, contraste y espaciado configurables",
            RuleCategory.Accessibility, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("low_vision.no_color_only_coding", "No transmitir información solo mediante color",
            RuleCategory.Accessibility, ApplyMode.Default, 1, RiskLevel.Low,
            devNotes: "No usar color como único código"),
        Rule("low_vision.alt_text_screen_reader", "Texto alternativo y compatibilidad lector de pantalla",
            RuleCategory.Accessibility, ApplyMode.Default, 1, RiskLevel.Low),

        // Discapacidad motora
        Rule("motor.minimize_nonessential_writing", "Minimizar escritura/manipulación no esencial",
            RuleCategory.Response, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("motor.keyboard_selection_voice", "Teclado/selección/voz según medidas",
            RuleCategory.Response, ApplyMode.Configurable, 2, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.MotorMode }, requiresReview: true,
            devNotes: "No aplicar si el modo motor es el constructo"),

        // Discapacidad intelectual / adaptación curricular
        Rule("curricular.never_infer_from_diagnosis", "No inferir nivel curricular por diagnóstico",
            RuleCategory.Curricular, ApplyMode.Always, 1, RiskLevel.Critical, requiresReview: true),
        Rule("curricular.use_teacher_defined_referents_only", "Usar únicamente nivel/referentes definidos por docente",
            RuleCategory.Curricular, ApplyMode.Always, 3, RiskLevel.Critical, requiresReview: true,
            devNotes: "Hard rule CURRICULAR_CHANGE. No sustituye prueba base"),

        // Comprensión lectora
        Rule("reading_comprehension.fragment_text_near_questions", "Fragmentar texto y acercar preguntas al fragmento",
            RuleCategory.Text, ApplyMode.Default, 2, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.TextStructure }, requiresReview: true,
            devNotes: "No aplicar si estructura textual es parte del constructo"),
        Rule("reading_comprehension.anticipate_vocabulary", "Anticipar vocabulario no evaluado",
            RuleCategory.Vocabulary, ApplyMode.Configurable, 2, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.Vocabulary }, requiresReview: true,
            devNotes: "No aplicar si vocabulario es el constructo"),

        // Procesamiento lento
        Rule("slow_processing.lower_density_per_page", "Menor densidad por página",
            RuleCategory.Layout, ApplyMode.Default, 1, RiskLevel.Low),
        Rule("slow_processing.configurable_time_pauses", "Tiempo/pausas configurables",
            RuleCategory.Time, ApplyMode.Configurable, 2, RiskLevel.Medium,
            requiresReview: true, devNotes: "Valor definido por docente"),

        // Funciones ejecutivas
        Rule("executive_functions.checklist_steps_progress", "Checklist + pasos 1-2-3 + progreso",
            RuleCategory.Planning, ApplyMode.Default, 1, RiskLevel.Low),

        // Castellano L2
        Rule("spanish_l2.visual_glossary_clear_language", "Glosario visual y lenguaje claro",
            RuleCategory.Language, ApplyMode.Default, 2, RiskLevel.Medium,
            blockedBy: new() { ConstructTags.LanguageDomain }, requiresReview: true,
            devNotes: "No aplicar si dominio lingüístico es el constructo"),
        Rule("spanish_l2.translation_only_if_authorized", "Traducción solo si autorizada",
            RuleCategory.Translation, ApplyMode.Never, 2, RiskLevel.High,
            blockedBy: new() { ConstructTags.LanguageDomain }, requiresReview: true,
            devNotes: "No afirmar una fuente 'especial' universal. No aplicar si idioma es el constructo"),

        // Altas capacidades
        Rule("gifted.optional_depth_extension", "Extensión opcional de transferencia/razonamiento",
            RuleCategory.Deepening, ApplyMode.Configurable, 2, RiskLevel.Low,
            requiresReview: true, devNotes: "No sustituye la prueba base ni sus 10 puntos"),
    };

    public static readonly IReadOnlyDictionary<string, AdaptationRule> ById =
        All.ToDictionary(r => r.Id, r => r);

    private static AdaptationRule Rule(
        string id, string description, RuleCategory category, ApplyMode applyMode,
        int minLevel, RiskLevel riskLevel, List<string>? blockedBy = null,
        bool requiresReview = false, string devNotes = "") => new()
    {
        Id = id,
        Description = description,
        Category = category,
        ApplyMode = applyMode,
        MinLevel = minLevel,
        RiskLevel = riskLevel,
        BlockedByConstructTags = blockedBy ?? new List<string>(),
        RequiresTeacherReview = requiresReview,
        DevNotes = devNotes
    };
}
