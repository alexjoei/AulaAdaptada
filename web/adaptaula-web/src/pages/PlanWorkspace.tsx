import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import type { AdaptationPlan, Assessment, AdaptedQuestion, ValidationResult, Question } from "../api/types";
import { Eyebrow, Stepper, SeverityBadge, PIPELINE_STEPS } from "../components/ui";
import { diffWords, type DiffToken } from "../utils/diff";

function DiffText({ tokens, side }: { tokens: DiffToken[]; side: "original" | "adapted" }) {
  return (
    <p style={{ marginTop: 8 }}>
      {tokens.map((t, i) => {
        if (side === "original" && t.type === "added") return null;
        if (side === "adapted" && t.type === "removed") return null;
        const className = t.type === "removed" ? "diff-removed" : t.type === "added" ? "diff-added" : undefined;
        return <span key={i} className={className}>{t.text}</span>;
      })}
    </p>
  );
}

export default function PlanWorkspace() {
  const { id: planId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [plan, setPlan] = useState<AdaptationPlan | null>(null);
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [adapted, setAdapted] = useState<AdaptedQuestion[]>([]);
  const [validation, setValidation] = useState<ValidationResult[]>([]);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [generated, setGenerated] = useState(false);

  useEffect(() => {
    if (!planId) return;
    api.plans.get(planId).then(async (p) => {
      setPlan(p);
      const a = await api.assessments.get(p.assessmentId);
      setAssessment(a);
      if (p.status !== "Draft" && p.status !== "Planned") {
        const [freshAdapted, freshValidation] = await Promise.all([
          api.plans.adaptedQuestions(planId),
          api.plans.validationResults(planId),
        ]);
        setAdapted(freshAdapted);
        setValidation(freshValidation);
        setGenerated(true);
      }
    });
  }, [planId]);

  async function generate() {
    if (!planId) return;
    setGenerating(true);
    setError(null);
    try {
      const result = await api.plans.generate(planId);
      setAdapted(result.adaptedQuestions);
      setValidation(result.validationResults);
      setGenerated(true);
    } catch {
      setError("No se pudo generar la adaptación. Comprueba la configuración de la IA (clave de Gemini).");
    } finally {
      setGenerating(false);
    }
  }

  async function review(adaptedQuestionId: string, approved: boolean) {
    if (!planId) return;
    await api.plans.review(planId, adaptedQuestionId, approved);
    const [freshAdapted, freshValidation] = await Promise.all([
      api.plans.adaptedQuestions(planId),
      api.plans.validationResults(planId),
    ]);
    setAdapted(freshAdapted);
    setValidation(freshValidation);
  }

  if (!plan || !assessment) return <div className="page wrap"><p className="muted">Cargando…</p></div>;

  const questionsById = new Map<string, Question>(assessment.sections.flatMap((s) => s.questions).map((q) => [q.id, q]));
  const validationByQuestion = new Map<string, ValidationResult[]>();
  for (const v of validation) {
    if (!v.questionId) continue;
    validationByQuestion.set(v.questionId, [...(validationByQuestion.get(v.questionId) ?? []), v]);
  }
  const diffByQuestion = new Map<string, DiffToken[]>();
  for (const a of adapted) {
    const original = questionsById.get(a.questionId)?.originalText ?? "";
    diffByQuestion.set(a.questionId, diffWords(original, a.adaptedText));
  }
  const planLevelIssues = validation.filter((v) => !v.questionId);
  const errorCount = validation.filter((v) => v.severity === "Error").length;

  const links = [
    "/upload",
    `/assessments/${assessment.id}/analysis`,
    `/assessments/${assessment.id}/adapt`,
    undefined,
    generated ? `/plans/${planId}/export` : undefined,
  ];

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>{generated ? "Comparador" : "Generación de textos"}</Eyebrow>
        <Stepper steps={PIPELINE_STEPS} current={3} links={links} />
        <h1 style={{ fontSize: 28, marginBottom: 4 }}>{assessment.title}</h1>
        <p className="muted" style={{ marginBottom: 24 }}>
          Nivel {plan.level} {plan.isCurricularChange && "· Cambio curricular"}
        </p>

        {plan.warnings.length > 0 && (
          <div className="card-panel" style={{ marginBottom: 24 }}>
            <h3 style={{ fontSize: 14, marginBottom: 8 }}>Avisos del plan</h3>
            <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13, color: "var(--text-secondary)" }}>
              {plan.warnings.map((w, i) => <li key={i}>{w}</li>)}
            </ul>
          </div>
        )}

        {!generated && (
          <div className="card-panel" style={{ textAlign: "center", padding: 40 }}>
            <p className="muted" style={{ marginBottom: 20 }}>
              El plan ya decidió qué adaptaciones aplicar. Ahora la IA reescribe el texto de cada
              pregunta siguiendo exactamente esas reglas — nunca cambia puntos ni respuestas.
            </p>
            <button className="btn" disabled={generating} onClick={generate}>
              {generating ? "Generando…" : "Generar textos adaptados"}
            </button>
            {error && <p style={{ color: "var(--error)", marginTop: 16 }}>{error}</p>}
          </div>
        )}

        {generated && (
          <>
            <div className="row spread card-panel" style={{ marginBottom: 24 }}>
              <div>
                <strong>{errorCount === 0 ? "Sin errores" : `${errorCount} error(es) de validación`}</strong>
                <p className="muted" style={{ fontSize: 13 }}>
                  Revisa los cambios resaltados y acepta o rechaza cada pregunta antes de continuar.
                </p>
              </div>
              <div className="row">
                {validation.map((v) => <SeverityBadge key={v.id} severity={v.severity} />)}
              </div>
            </div>

            {planLevelIssues.length > 0 && (
              <div className="card-panel" style={{ marginBottom: 24 }}>
                {planLevelIssues.map((v) => (
                  <p key={v.id} style={{ fontSize: 13, marginBottom: 6 }}>
                    <SeverityBadge severity={v.severity} /> <span style={{ marginLeft: 8 }}>{v.message}</span>
                  </p>
                ))}
              </div>
            )}

            <div className="comparator-grid">
              <div>
                <div className="comparator-col-label">Original</div>
                {[...questionsById.values()].sort((a, b) => a.order - b.order).map((q) => {
                  const tokens = diffByQuestion.get(q.id);
                  return (
                    <div key={q.id} className="comparator-question">
                      <strong>Pregunta {q.order + 1} · {q.points} pts</strong>
                      {tokens ? <DiffText tokens={tokens} side="original" /> : <p style={{ marginTop: 8 }}>{q.originalText}</p>}
                    </div>
                  );
                })}
              </div>
              <div>
                <div className="comparator-col-label">Adaptado</div>
                {adapted.sort((a, b) => (questionsById.get(a.questionId)?.order ?? 0) - (questionsById.get(b.questionId)?.order ?? 0)).map((a) => {
                  const q = questionsById.get(a.questionId);
                  const issues = validationByQuestion.get(a.questionId) ?? [];
                  const tokens = diffByQuestion.get(a.questionId);
                  return (
                    <div key={a.id} className="comparator-question adapted">
                      <div className="row spread">
                        <strong>Pregunta {(q?.order ?? 0) + 1} · {a.points} pts</strong>
                        <div className="row">{issues.map((v) => <SeverityBadge key={v.id} severity={v.severity} />)}</div>
                      </div>
                      {tokens ? <DiffText tokens={tokens} side="adapted" /> : <p style={{ marginTop: 8 }}>{a.adaptedText}</p>}
                      {a.supports.length > 0 && (
                        <ul className="support-list">
                          {a.supports.map((s, i) => <li key={i}>{s}</li>)}
                        </ul>
                      )}
                      <div className="change-actions">
                        <button className="btn-sm btn-outline" onClick={() => review(a.id, true)}>
                          {a.teacherApproved ? "✓ Aprobado" : "Aceptar"}
                        </button>
                        <button className="btn-sm btn-outline" onClick={() => review(a.id, false)}>Rechazar (usar original)</button>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            <button className="btn mt-32" onClick={() => navigate(`/plans/${planId}/export`)}>
              Continuar a generación
            </button>
          </>
        )}
      </div>
    </div>
  );
}
