import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import type { Assessment, Question } from "../api/types";
import { Eyebrow, Stepper } from "../components/ui";

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

const LOCK_OPTIONS = [
  { key: "content", label: "Contenido" },
  { key: "criteria", label: "Criterios" },
  { key: "total_points", label: "Puntuación total" },
  { key: "language", label: "Idioma" },
  { key: "grade", label: "Curso" },
];

export default function Analysis() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [dirtyQuestions, setDirtyQuestions] = useState<Record<string, Partial<Question>>>({});
  const [locks, setLocks] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!id) return;
    api.assessments.get(id).then((a) => { setAssessment(a); setLocks(a.lockedFields); });
  }, [id]);

  if (!assessment) return <div className="page wrap"><p className="muted">Cargando…</p></div>;

  const lowConfidence = (assessment.extractionConfidence ?? 1) < 0.6;
  const questions = assessment.sections.flatMap((s) => s.questions);

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

  async function saveAndContinue() {
    if (!id) return;
    setSaving(true);
    try {
      const edits = Object.entries(dirtyQuestions).map(([qid, patch]) => ({
        id: qid,
        points: patch.points,
        expectedAnswer: patch.expectedAnswer ?? undefined,
        constructTags: patch.constructTags,
      }));
      await api.assessments.update(id, { lockedFields: locks, questions: edits });
      navigate(`/assessments/${id}/adapt`);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Análisis del original</Eyebrow>
        <Stepper steps={["Subida", "Análisis", "Adaptación", "Comparador"]} current={1} />
        <h1 style={{ fontSize: 28, marginBottom: 4 }}>{assessment.title}</h1>
        <p className="muted" style={{ marginBottom: 24 }}>
          {assessment.subject} · {assessment.grade}º · {questions.length} preguntas
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

        <div className="card-panel" style={{ marginBottom: 24 }}>
          <h3 style={{ fontSize: 16, marginBottom: 12 }}>Bloqueos del docente</h3>
          <p className="muted" style={{ fontSize: 13, marginBottom: 12 }}>
            Estos campos nunca cambiarán en ninguna adaptación generada a partir de esta prueba.
          </p>
          <div className="row">
            {LOCK_OPTIONS.map((opt) => (
              <label key={opt.key} className="checkbox-row">
                <input type="checkbox" checked={locks.includes(opt.key)} onChange={() => toggleLock(opt.key)} />
                {opt.label}
              </label>
            ))}
          </div>
        </div>

        <div className="stack">
          {questions.map((q, i) => {
            const patch = dirtyQuestions[q.id] ?? {};
            const tags = patch.constructTags ?? q.constructTags;
            return (
              <div key={q.id} className="question-row">
                <div className="question-row-header">
                  <strong>Pregunta {i + 1}</strong>
                  <div className="row">
                    <label style={{ fontSize: 13 }}>Puntos:</label>
                    <input
                      type="number" style={{ width: 70, padding: "6px 10px" }}
                      value={patch.points ?? q.points}
                      onChange={(e) => patchQuestion(q.id, { points: Number(e.target.value) })}
                    />
                  </div>
                </div>
                <p className="question-text">{q.originalText}</p>
                <div className="field" style={{ marginBottom: 10 }}>
                  <label style={{ fontSize: 12 }}>Respuesta esperada (opcional; nunca se muestra a la IA)</label>
                  <input
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
                      <span
                        key={tag.key}
                        className={`tag-chip${tags.includes(tag.key) ? " selected" : ""}`}
                        onClick={() => toggleTag(q, tag.key)}
                      >
                        {tag.label}
                      </span>
                    ))}
                  </div>
                </div>
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
