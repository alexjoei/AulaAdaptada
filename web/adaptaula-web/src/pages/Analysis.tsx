import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import type { Assessment, Curriculum, CurriculumSummary, Question, QuestionAnalysis } from "../api/types";
import { Eyebrow, Stepper, PIPELINE_STEPS, Callout } from "../components/ui";

const CONSTRUCT_TAGS = [
  { key: "reading", label: "Lectura" },
  { key: "spelling", label: "Ortografía" },
  { key: "writing", label: "Escritura manual" },
  { key: "calculation", label: "Cálculo" },
  { key: "memory", label: "Memoria" },
  { key: "linguistic_complexity", label: "Complejidad lingüística" },
  { key: "motor_mode", label: "Modo motor" },
  { key: "text_structure", label: "Estructura textual" },
  { key: "vocabulary", label: "Vocabulario" },
  { key: "language_domain", label: "Dominio lingüístico" },
  { key: "figurative_language", label: "Lenguaje figurado" },
  { key: "listening_comprehension", label: "Comprensión auditiva" },
];

/** A section's stimulusText can contain "\0IMG:<index>\0" markers — placed by the ingestion
 * pipeline at the exact point an image occurs in the original reading flow — referencing that
 * section's own assetRefs by position. Rendering them inline (rather than as a separate gallery
 * below the text) keeps the passage looking like the original document, which matters here: a
 * caption, a labelled diagram or a photo next to a specific sentence is often part of what the
 * question is actually asking about. */
const IMAGE_MARKER = /IMG:(\d+)/g;

function renderInterleavedContent(text: string, assetRefs: string[], altPrefix: string) {
  const parts = text.split(IMAGE_MARKER);
  return parts.map((part, i) => {
    if (i % 2 === 1) {
      const uri = assetRefs[Number(part)];
      return uri ? <img key={i} src={uri} alt={`${altPrefix} ${Math.floor(i / 2) + 1}`} className="inline-content-image" /> : null;
    }
    return part.trim() ? <p key={i} className="question-text" style={{ marginBottom: 10 }}>{part.trim()}</p> : null;
  });
}

/** Protected elements (V2 §8): everything the teacher can freeze before generating. */
const LOCK_OPTIONS = [
  { key: "content", label: "Contenido evaluado", hint: "Ninguna pregunta puede perder sus ideas clave." },
  { key: "criteria", label: "Criterio de evaluación", hint: "Cada criterio vinculado sigue evaluándose." },
  { key: "correct_answer", label: "Respuesta correcta", hint: "Ni se modifica ni se insinúa en pistas o apoyos." },
  { key: "total_points", label: "Puntuación", hint: "La puntuación total y la de cada pregunta no cambian." },
  { key: "language", label: "Idioma", hint: "El enunciado no cambia de idioma (salvo traducción autorizada)." },
  { key: "essential_vocabulary", label: "Vocabulario curricular esencial", hint: "Los términos que indiques deben aparecer tal cual." },
  { key: "question_count", label: "Número de preguntas", hint: "No se elimina ni se añade ninguna pregunta." },
  { key: "cognitive_demand", label: "Nivel / demanda cognitiva", hint: "No se acepta un texto mucho más corto que rebaje la exigencia." },
  { key: "grade", label: "Curso", hint: "El curso de la prueba no cambia." },
];

const COGNITIVE = ["recordar", "comprender", "aplicar", "analizar", "evaluar", "crear"];
const LOADS = ["baja", "media", "alta"];

const BLANK_ANALYSIS: QuestionAnalysis = {
  content: "", skill: "", cognitiveDemand: "", linguisticDemand: "", readingLoad: "", writingLoad: "", executiveLoad: "",
  criteriaIds: [], contentIds: [], source: "teacher", confirmed: false,
};

export default function Analysis() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [dirtyQuestions, setDirtyQuestions] = useState<Record<string, Partial<Question>>>({});
  const [analysisEdits, setAnalysisEdits] = useState<Record<string, QuestionAnalysis>>({});
  const [locks, setLocks] = useState<string[]>([]);
  const [vocabulary, setVocabulary] = useState<string[]>([]);
  const [vocabInput, setVocabInput] = useState("");
  const [curriculumId, setCurriculumId] = useState("");
  const [areaId, setAreaId] = useState("");
  const [summaries, setSummaries] = useState<CurriculumSummary[]>([]);
  const [curriculum, setCurriculum] = useState<Curriculum | null>(null);
  const [saving, setSaving] = useState(false);
  const [analyzing, setAnalyzing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [openAnalysis, setOpenAnalysis] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    api.assessments.get(id).then((a) => {
      setAssessment(a);
      setLocks(a.lockedFields);
      setVocabulary(a.protectedVocabulary);
      setCurriculumId(a.curriculumId ?? "");
      setAreaId(a.curriculumAreaId ?? "");
    }).catch(() => setError("No se pudo cargar la prueba."));
    api.curriculum.list().then(setSummaries).catch(() => setSummaries([]));
  }, [id]);

  useEffect(() => {
    if (!curriculumId) { setCurriculum(null); return; }
    api.curriculum.get(curriculumId).then(setCurriculum).catch(() => setCurriculum(null));
  }, [curriculumId]);

  const criteriaById = useMemo(() => new Map((curriculum?.areas ?? []).flatMap((a) => a.criteria).map((c) => [c.id, c])), [curriculum]);
  const contentsById = useMemo(() => new Map((curriculum?.areas ?? []).flatMap((a) => a.contents).map((c) => [c.id, c])), [curriculum]);
  const area = curriculum?.areas.find((a) => a.id === areaId) ?? null;
  const cycle = curriculum && assessment?.grade ? curriculum.cycles.find((c) => c.grades.includes(assessment.grade!))?.id : undefined;
  const areaCriteria = (area?.criteria ?? []).filter((c) => !cycle || c.cycle === cycle);
  const areaContents = (area?.contents ?? []).filter((c) => !cycle || c.cycle === cycle);

  if (!assessment) return <div className="page wrap"><p className="muted">{error ?? "Cargando…"}</p></div>;

  const lowConfidence = (assessment.extractionConfidence ?? 1) < 0.6;
  const questions = assessment.sections.flatMap((s) => s.questions);
  const sections = [...assessment.sections].sort((a, b) => a.order - b.order);

  function patchQuestion(qid: string, patch: Partial<Question>) {
    setDirtyQuestions((prev) => ({ ...prev, [qid]: { ...prev[qid], ...patch } }));
  }

  function toggleTag(q: Question, tag: string) {
    const current = dirtyQuestions[q.id]?.constructTags ?? q.constructTags;
    const next = current.includes(tag) ? current.filter((t) => t !== tag) : [...current, tag];
    patchQuestion(q.id, { constructTags: next });
  }

  function toggleLock(key: string) {
    setLocks((prev) => (prev.includes(key) ? prev.filter((l) => l !== key) : [...prev, key]));
  }

  function addVocab() {
    const terms = vocabInput.split(",").map((t) => t.trim()).filter(Boolean);
    if (terms.length === 0) return;
    setVocabulary((prev) => [...new Set([...prev, ...terms])]);
    if (!locks.includes("essential_vocabulary")) setLocks((prev) => [...prev, "essential_vocabulary"]);
    setVocabInput("");
  }

  function currentAnalysis(q: Question): QuestionAnalysis | null {
    return analysisEdits[q.id] ?? q.analysis;
  }

  function editAnalysis(q: Question, change: Partial<QuestionAnalysis>) {
    const base = currentAnalysis(q) ?? BLANK_ANALYSIS;
    setAnalysisEdits((prev) => ({ ...prev, [q.id]: { ...base, ...change, source: "teacher" } }));
  }

  function buildUpdate() {
    return {
      lockedFields: locks,
      protectedVocabulary: vocabulary,
      curriculumId, curriculumAreaId: areaId,
      questions: questions
        .filter((q) => dirtyQuestions[q.id] || analysisEdits[q.id])
        .map((q) => {
          const patch = dirtyQuestions[q.id] ?? {};
          return {
            id: q.id,
            points: patch.points,
            expectedAnswer: patch.expectedAnswer ?? undefined,
            constructTags: patch.constructTags,
            analysis: analysisEdits[q.id],
          };
        }),
    };
  }

  async function saveAndContinue() {
    if (!id) return;
    setSaving(true);
    setError(null);
    try {
      await api.assessments.update(id, buildUpdate());
      navigate(`/assessments/${id}/adapt`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar.");
    } finally {
      setSaving(false);
    }
  }

  async function analyze() {
    if (!id) return;
    setAnalyzing(true);
    setError(null);
    try {
      await api.assessments.update(id, buildUpdate());
      const updated = await api.assessments.analyze(id);
      setAssessment(updated);
      setDirtyQuestions({});
      setAnalysisEdits({});
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo analizar la prueba.");
    } finally {
      setAnalyzing(false);
    }
  }

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Análisis del original</Eyebrow>
        <Stepper steps={PIPELINE_STEPS} current={1} links={["/upload"]} />
        <h1 style={{ fontSize: 28, marginBottom: 4 }}>{assessment.title}</h1>
        <p className="muted" style={{ marginBottom: 24 }}>
          {[assessment.subject, assessment.grade ? `${assessment.grade}º` : null, `${questions.length} preguntas`]
            .filter(Boolean).join(" · ")}
        </p>

        {lowConfidence && (
          <div className="card-panel mt-24" style={{ borderColor: "var(--warning)", marginBottom: 24 }}>
            <span className="badge badge-warning">Extracción de baja confianza</span>
            <p className="muted" style={{ marginTop: 8 }}>
              La detección automática de preguntas no fue muy fiable en este documento. Revisa que
              cada pregunta, sus puntos y la respuesta esperada sean correctos antes de continuar.
            </p>
          </div>
        )}

        <div className="card-panel" style={{ marginBottom: 24 }} data-testid="protected-elements">
          <h3 style={{ fontSize: 16, marginBottom: 6 }}>Elementos protegidos</h3>
          <p className="muted" style={{ fontSize: 13, marginBottom: 14 }}>
            Lo que marques aquí no cambiará en ninguna adaptación de esta prueba: si algo lo altera, la validación lo marca como error y no se podrá exportar.
          </p>
          <div className="card-grid cols-3">
            {LOCK_OPTIONS.map((opt) => (
              <label key={opt.key} className="checkbox-row" style={{ alignItems: "flex-start" }} data-lock={opt.key}>
                <input type="checkbox" checked={locks.includes(opt.key)} onChange={() => toggleLock(opt.key)} style={{ marginTop: 3 }} />
                <span>
                  <strong style={{ display: "block", color: "var(--text)" }}>{opt.label}</strong>
                  <span style={{ fontSize: 12 }} className="muted">{opt.hint}</span>
                </span>
              </label>
            ))}
          </div>

          <div className="field" style={{ marginTop: 18, marginBottom: 0 }}>
            <label htmlFor="vocab-input">Vocabulario curricular esencial (separa los términos con comas)</label>
            <div className="row">
              <input
                id="vocab-input" value={vocabInput} style={{ flex: 1, minWidth: 220 }} placeholder="carbohidratos, proteínas"
                onChange={(e) => setVocabInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); addVocab(); } }}
              />
              <button type="button" className="btn btn-sm btn-outline" onClick={addVocab}>Añadir</button>
            </div>
            <div className="row" style={{ marginTop: 8 }} data-testid="vocabulary-list">
              {vocabulary.map((term) => (
                <span key={term} className="tag-chip selected">
                  {term}
                  <button
                    type="button" aria-label={`Quitar ${term}`} onClick={() => setVocabulary((prev) => prev.filter((t) => t !== term))}
                    style={{ background: "none", border: "none", color: "inherit", cursor: "pointer", padding: 0 }}
                  >×</button>
                </span>
              ))}
            </div>
          </div>
        </div>

        <div className="card-panel" style={{ marginBottom: 24 }} data-testid="curriculum-panel">
          <h3 style={{ fontSize: 16, marginBottom: 6 }}>Análisis curricular (LOMLOE)</h3>
          <p className="muted" style={{ fontSize: 13, marginBottom: 14 }}>
            La IA propone, pregunta por pregunta, el contenido, la habilidad, las demandas y los criterios y saberes relacionados. Tú confirmas o modificas.
            El currículo es una capa independiente: nunca cambia por un diagnóstico.
          </p>
          {curriculum && !curriculum.verified && (
            <div style={{ marginBottom: 14 }}>
              <Callout kind="warning"><strong>Datos a verificar.</strong> {curriculum.note}</Callout>
            </div>
          )}
          <div className="row">
            <div className="field" style={{ marginBottom: 0 }}>
              <label htmlFor="an-curriculum">Currículo</label>
              <select id="an-curriculum" value={curriculumId} onChange={(e) => { setCurriculumId(e.target.value); setAreaId(""); }}>
                <option value="">— Sin currículo —</option>
                {summaries.map((s) => <option key={s.id} value={s.id}>{s.stage} · {s.ccaa}</option>)}
              </select>
            </div>
            <div className="field" style={{ marginBottom: 0 }}>
              <label htmlFor="an-area">Área de la prueba</label>
              <select id="an-area" value={areaId} disabled={!curriculum} onChange={(e) => setAreaId(e.target.value)}>
                <option value="">— Elige —</option>
                {curriculum?.areas.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
              </select>
            </div>
            <button type="button" className="btn btn-sm" disabled={!curriculumId || !areaId || analyzing || !assessment.grade} onClick={analyze}>
              {analyzing ? "Analizando…" : "Proponer análisis con IA"}
            </button>
          </div>
          {!assessment.grade && curriculumId && (
            <p className="field-hint" style={{ marginTop: 8 }}>Indica el curso de la prueba al subirla para acotar los criterios al ciclo correspondiente.</p>
          )}
        </div>

        {error && <div style={{ marginBottom: 16 }}><Callout kind="error">{error}</Callout></div>}

        <div className="stack">
          {sections.map((section, sectionIndex) => {
            const questionRows = [...section.questions].sort((a, b) => a.order - b.order).map((q) => {
              const patch = dirtyQuestions[q.id] ?? {};
              const tags = patch.constructTags ?? q.constructTags;
              const analysis = currentAnalysis(q);
              const open = openAnalysis === q.id;
              return (
                <div key={q.id} className="question-row" data-question={q.order + 1}>
                  <div className="question-row-header">
                    <strong>Pregunta {q.order + 1}</strong>
                    <div className="row">
                      <label htmlFor={`pts-${q.id}`} style={{ fontSize: 13 }}>Puntos:</label>
                      <input
                        id={`pts-${q.id}`} type="number" style={{ width: 70, padding: "6px 10px" }}
                        value={patch.points ?? q.points}
                        onChange={(e) => patchQuestion(q.id, { points: Number(e.target.value) })}
                      />
                    </div>
                  </div>
                  <p className="question-text">{q.originalText}</p>
                  {q.assetRefs.length > 0 && (
                    <div className="image-gallery" style={{ marginBottom: 10 }}>
                      {q.assetRefs.map((uri, i) => (
                        <img key={i} src={uri} alt={`Imagen de la pregunta ${q.order + 1}`} />
                      ))}
                    </div>
                  )}
                  <div className="field" style={{ marginBottom: 10 }}>
                    <label htmlFor={`ans-${q.id}`} style={{ fontSize: 12 }}>Respuesta esperada (opcional; nunca se muestra a la IA)</label>
                    <input
                      id={`ans-${q.id}`}
                      value={patch.expectedAnswer ?? q.expectedAnswer ?? ""}
                      onChange={(e) => patchQuestion(q.id, { expectedAnswer: e.target.value })}
                    />
                  </div>
                  <div>
                    <label style={{ fontSize: 12, fontWeight: 600, display: "block", marginBottom: 6 }}>
                      ¿Qué mide esta pregunta? (constructo evaluado)
                    </label>
                    <div className="row">
                      {CONSTRUCT_TAGS.map((tag) => (
                        <button
                          type="button" key={tag.key} aria-pressed={tags.includes(tag.key)}
                          className={`tag-chip${tags.includes(tag.key) ? " selected" : ""}`}
                          onClick={() => toggleTag(q, tag.key)}
                        >
                          {tag.label}
                        </button>
                      ))}
                    </div>
                  </div>

                  <div style={{ marginTop: 14, paddingTop: 12, borderTop: "1px dashed var(--border)" }}>
                    <div className="row spread">
                      <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
                        Análisis curricular{" "}
                        {analysis
                          ? <span className={`badge ${analysis.confirmed ? "badge-success" : "badge-review"}`}>{analysis.confirmed ? "Confirmado" : analysis.source === "ai" ? "Propuesta de la IA" : "Editado"}</span>
                          : <span className="badge">Sin analizar</span>}
                      </span>
                      <button type="button" className="btn btn-sm btn-outline" onClick={() => setOpenAnalysis(open ? null : q.id)}>
                        {open ? "Ocultar" : analysis ? "Revisar" : "Rellenar a mano"}
                      </button>
                    </div>

                    {open && (
                      <div style={{ marginTop: 12 }} data-testid={`analysis-${q.order + 1}`}>
                        <div className="row">
                          <div className="field" style={{ flex: 1, minWidth: 220 }}>
                            <label>Contenido evaluado</label>
                            <input value={analysis?.content ?? ""} onChange={(e) => editAnalysis(q, { content: e.target.value })} />
                          </div>
                          <div className="field" style={{ flex: 1, minWidth: 220 }}>
                            <label>Habilidad</label>
                            <input value={analysis?.skill ?? ""} onChange={(e) => editAnalysis(q, { skill: e.target.value })} />
                          </div>
                        </div>
                        <div className="row">
                          {([
                            ["cognitiveDemand", "Demanda cognitiva", COGNITIVE],
                            ["linguisticDemand", "Demanda lingüística", LOADS],
                            ["readingLoad", "Carga lectora", LOADS],
                            ["writingLoad", "Carga escritora", LOADS],
                            ["executiveLoad", "Carga ejecutiva", LOADS],
                          ] as const).map(([field, label, options]) => (
                            <div key={field} className="field" style={{ marginBottom: 8 }}>
                              <label>{label}</label>
                              <select value={analysis?.[field] ?? ""} onChange={(e) => editAnalysis(q, { [field]: e.target.value })}>
                                <option value="">—</option>
                                {options.map((o) => <option key={o} value={o}>{o}</option>)}
                              </select>
                            </div>
                          ))}
                        </div>

                        <div className="field">
                          <label>Criterios de evaluación relacionados</label>
                          <div className="row">
                            {(analysis?.criteriaIds ?? []).map((cid) => (
                              <span key={cid} className="tag-chip selected" title={criteriaById.get(cid)?.text}>
                                {cid}
                                <button type="button" aria-label={`Quitar ${cid}`} style={{ background: "none", border: "none", color: "inherit", cursor: "pointer", padding: 0 }}
                                  onClick={() => editAnalysis(q, { criteriaIds: (analysis?.criteriaIds ?? []).filter((x) => x !== cid) })}>×</button>
                              </span>
                            ))}
                          </div>
                          <select
                            value="" aria-label="Añadir criterio" disabled={!area}
                            onChange={(e) => e.target.value && editAnalysis(q, { criteriaIds: [...new Set([...(analysis?.criteriaIds ?? []), e.target.value])] })}
                          >
                            <option value="">+ Añadir criterio…</option>
                            {areaCriteria.map((c) => <option key={c.id} value={c.id}>{c.id} — {c.text.slice(0, 90)}</option>)}
                          </select>
                        </div>

                        <div className="field">
                          <label>Contenidos / saberes relacionados</label>
                          <div className="row">
                            {(analysis?.contentIds ?? []).map((cid) => (
                              <span key={cid} className="tag-chip selected" title={contentsById.get(cid)?.text}>
                                {contentsById.get(cid)?.block.split(".")[0] ?? ""} · {(contentsById.get(cid)?.text ?? cid).slice(0, 40)}
                                <button type="button" aria-label={`Quitar ${cid}`} style={{ background: "none", border: "none", color: "inherit", cursor: "pointer", padding: 0 }}
                                  onClick={() => editAnalysis(q, { contentIds: (analysis?.contentIds ?? []).filter((x) => x !== cid) })}>×</button>
                              </span>
                            ))}
                          </div>
                          <select
                            value="" aria-label="Añadir saber" disabled={!area}
                            onChange={(e) => e.target.value && editAnalysis(q, { contentIds: [...new Set([...(analysis?.contentIds ?? []), e.target.value])] })}
                          >
                            <option value="">+ Añadir saber…</option>
                            {areaContents.map((c) => <option key={c.id} value={c.id}>{c.block.split(".")[0]} — {c.text.slice(0, 90)}</option>)}
                          </select>
                        </div>

                        <label className="checkbox-row">
                          <input type="checkbox" checked={analysis?.confirmed ?? false} onChange={(e) => editAnalysis(q, { confirmed: e.target.checked })} />
                          Confirmo este análisis
                        </label>
                      </div>
                    )}
                  </div>
                </div>
              );
            });

            if (!section.stimulusText) {
              return <div key={section.id} className="stack">{questionRows}</div>;
            }

            return (
              <div key={section.id} className="section-group">
                <div className="section-group-label">Grupo {sectionIndex + 1} · {section.questions.length} preguntas relacionadas</div>
                <div className="card-panel stimulus-panel">
                  <span className="eyebrow" style={{ marginBottom: 10 }}>Enunciado / texto de referencia</span>
                  {renderInterleavedContent(section.stimulusText, section.assetRefs, `Imagen del grupo ${sectionIndex + 1}`)}
                </div>
                <div className="stack">{questionRows}</div>
              </div>
            );
          })}
        </div>

        <button className="btn mt-32" disabled={saving} onClick={saveAndContinue}>
          {saving ? "Guardando…" : "Continuar a adaptación"}
        </button>
      </div>
    </div>
  );
}
