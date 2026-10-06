import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api } from "../api/client";
import type { Assessment, ChangeLogRecord, Question } from "../api/types";
import { Eyebrow, EmptyState } from "../components/ui";

const KIND_LABEL: Record<string, string> = {
  generated: "Adaptación generada", proposal: "Propuesta de la IA", review: "Revisión de pregunta", undo: "Cambio deshecho",
  edit: "Edición manual", text_tool: "Herramienta de IA", document: "Editor visual", export: "Exportación",
};
const DECISION_LABEL: Record<string, string> = { Pending: "Pendiente", Accepted: "Aceptado", Rejected: "Rechazado", Edited: "Editado" };
const RISK_LABEL: Record<string, string> = { Low: "Bajo", Medium: "Medio", High: "Alto", Critical: "Crítico" };

/** Audit trail (V2 §21): before/after, rule applied, reason, risk, the teacher's decision and the date. */
export default function History() {
  const { id: planId } = useParams<{ id: string }>();
  const [records, setRecords] = useState<ChangeLogRecord[] | null>(null);
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [kind, setKind] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!planId) return;
    (async () => {
      try {
        const plan = await api.plans.get(planId);
        const [log, a] = await Promise.all([api.plans.changelog(planId), api.assessments.get(plan.assessmentId)]);
        setRecords(log);
        setAssessment(a);
      } catch {
        setError("No se pudo cargar el historial.");
      }
    })();
  }, [planId]);

  const questions = useMemo(
    () => new Map<string, Question>((assessment?.sections ?? []).flatMap((s) => s.questions).map((q) => [q.id, q])), [assessment]);

  if (!records) return <div className="page wrap"><p className="muted">{error ?? "Cargando…"}</p></div>;

  const shown = records.filter((r) => !kind || r.kind === kind);

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Historial y trazabilidad</Eyebrow>
        <h1 style={{ fontSize: 28, marginBottom: 6 }}>Historial de cambios</h1>
        <p className="muted" style={{ marginBottom: 20, maxWidth: 700 }}>
          Por qué esta prueba quedó como quedó: cada cambio con su antes y después, la medida que lo provocó, el motivo, el riesgo, tu decisión y la fecha.
          {" "}<Link to={`/plans/${planId}`}>← Volver al comparador</Link>
        </p>

        <div className="row" style={{ marginBottom: 16 }}>
          <label htmlFor="kind-filter" style={{ fontSize: 13 }}>Filtrar:</label>
          <select id="kind-filter" value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="">Todo ({records.length})</option>
            {Object.entries(KIND_LABEL).map(([k, label]) => (
              <option key={k} value={k}>{label} ({records.filter((r) => r.kind === k).length})</option>
            ))}
          </select>
        </div>

        {shown.length === 0 ? (
          <EmptyState title="Sin movimientos" description="Cuando generes, revises o edites la adaptación, aparecerá aquí." />
        ) : (
          <div className="card-panel" style={{ overflowX: "auto" }}>
            <table className="data-table" data-testid="history-table">
              <thead>
                <tr><th>Fecha</th><th>Qué</th><th>Pregunta</th><th>Medida / regla</th><th>Antes</th><th>Después</th><th>Motivo</th><th>Riesgo</th><th>Decisión</th></tr>
              </thead>
              <tbody>
                {shown.map((r) => (
                  <tr key={r.id} data-kind={r.kind}>
                    <td style={{ whiteSpace: "nowrap" }}>{new Date(r.at).toLocaleString("es-ES", { dateStyle: "short", timeStyle: "short" })}</td>
                    <td>{KIND_LABEL[r.kind] ?? r.kind}</td>
                    <td>{r.questionId && questions.get(r.questionId) ? questions.get(r.questionId)!.order + 1 : "—"}</td>
                    <td><code style={{ fontSize: 11 }}>{r.ruleId || "—"}</code></td>
                    <td style={{ maxWidth: 220, whiteSpace: "pre-wrap" }}>{r.before.slice(0, 160) || "—"}</td>
                    <td style={{ maxWidth: 220, whiteSpace: "pre-wrap" }}>{r.after.slice(0, 160) || "—"}</td>
                    <td style={{ maxWidth: 240 }}>{r.reason}{r.actor && <div className="muted">por {r.actor}</div>}</td>
                    <td>{RISK_LABEL[r.risk]}</td>
                    <td>{DECISION_LABEL[r.decision]}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
