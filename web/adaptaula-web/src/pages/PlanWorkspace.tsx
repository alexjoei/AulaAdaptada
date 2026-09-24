import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AdaptationPlan, Assessment, AdaptedQuestion, ValidationResult, Question } from "../api/types";
import { Eyebrow, Stepper, SeverityBadge } from "../components/ui";

export default function PlanWorkspace() {
  const { id: planId } = useParams<{ id: string }>();
  const [plan, setPlan] = useState<AdaptationPlan | null>(null);
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [adapted, setAdapted] = useState<AdaptedQuestion[]>([]);
  const [validation, setValidation] = useState<ValidationResult[]>([]);
  const [canExport, setCanExport] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [approvedBy, setApprovedBy] = useState("");
  const [exporting, setExporting] = useState<"Docx" | "Pdf" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [generated, setGenerated] = useState(false);

  useEffect(() => {
    if (!planId) return;
    api.plans.get(planId).then(async (p) => {
      setPlan(p);
      const a = await api.assessments.get(p.assessmentId);
      setAssessment(a);
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
      setCanExport(result.canExport);
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

  async function doExport(format: "Docx" | "Pdf") {
    if (!planId) return;
    setExporting(format);
    setError(null);
    try {
      await api.plans.export(planId, format, approvedBy);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "No se pudo exportar.");
    } finally {
      setExporting(null);
    }
  }

  if (!plan || !assessment) return <div className="page wrap"><p className="muted">Cargando…</p></div>;

  const questionsById = new Map<string, Question>(assessment.sections.flatMap((s) => s.questions).map((q) => [q.id, q]));
  const validationByQuestion = new Map<string, ValidationResult[]>();
  for (const v of validation) {
    if (!v.questionId) continue;
    validationByQuestion.set(v.questionId, [...(validationByQuestion.get(v.questionId) ?? []), v]);
  }
  const planLevelIssues = validation.filter((v) => !v.questionId);
  const errorCount = validation.filter((v) => v.severity === "Error").length;

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>{generated ? "Comparador" : "Generación"}</Eyebrow>
        <Stepper steps={["Subida", "Análisis", "Adaptación", "Comparador"]} current={3} />
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
                  {canExport ? "Se puede exportar." : "Resuelve los errores antes de exportar."}
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
                {[...questionsById.values()].sort((a, b) => a.order - b.order).map((q) => (
                  <div key={q.id} className="comparator-question">
                    <strong>Pregunta {q.order + 1} · {q.points} pts</strong>
                    <p style={{ marginTop: 8 }}>{q.originalText}</p>
                  </div>
                ))}
              </div>
              <div>
                <div className="comparator-col-label">Adaptado</div>
                {adapted.sort((a, b) => (questionsById.get(a.questionId)?.order ?? 0) - (questionsById.get(b.questionId)?.order ?? 0)).map((a) => {
                  const q = questionsById.get(a.questionId);
                  const issues = validationByQuestion.get(a.questionId) ?? [];
                  return (
                    <div key={a.id} className="comparator-question adapted">
                      <div className="row spread">
                        <strong>Pregunta {(q?.order ?? 0) + 1} · {a.points} pts</strong>
                        <div className="row">{issues.map((v) => <SeverityBadge key={v.id} severity={v.severity} />)}</div>
                      </div>
                      <p style={{ marginTop: 8 }}>{a.adaptedText}</p>
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

            <div className="card-panel mt-32">
              <h3 style={{ fontSize: 16, marginBottom: 12 }}>Exportar</h3>
              <div className="field" style={{ maxWidth: 320 }}>
                <label>Aprobado por (docente)</label>
                <input value={approvedBy} onChange={(e) => setApprovedBy(e.target.value)} placeholder="Nombre o alias del docente" />
              </div>
              <div className="row">
                <button className="btn" disabled={!canExport || !!exporting} onClick={() => doExport("Docx")}>
                  {exporting === "Docx" ? "Exportando…" : "Exportar DOCX"}
                </button>
                <button className="btn btn-outline" disabled={!canExport || !!exporting} onClick={() => doExport("Pdf")}>
                  {exporting === "Pdf" ? "Exportando…" : "Exportar PDF"}
                </button>
              </div>
              {error && <p style={{ color: "var(--error)", marginTop: 12 }}>{error}</p>}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
