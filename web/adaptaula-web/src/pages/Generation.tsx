import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AdaptationPlan, Assessment, ExportBundleOptions, PlanState } from "../api/types";
import { Callout, Eyebrow, SemaphoreBadge, SeverityBadge, Stepper, PIPELINE_STEPS, VALIDATION_LABELS } from "../components/ui";
import ExportOptions, { DEFAULT_OPTIONS } from "../components/ExportOptions";

export default function Generation() {
  const { id: planId } = useParams<{ id: string }>();
  const [plan, setPlan] = useState<AdaptationPlan | null>(null);
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [state, setState] = useState<PlanState | null>(null);
  const [approvedBy, setApprovedBy] = useState("");
  const [options, setOptions] = useState<ExportBundleOptions>({ ...DEFAULT_OPTIONS, includeOriginal: false });
  const [busy, setBusy] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!planId) return;
    (async () => {
      try {
        const p = await api.plans.get(planId);
        setPlan(p);
        const [a, s] = await Promise.all([api.assessments.get(p.assessmentId), api.plans.state(planId)]);
        setAssessment(a);
        setState(s);
      } catch {
        setError("No se pudo cargar la adaptación.");
      }
    })();
  }, [planId]);

  async function run(label: string, action: () => Promise<string>) {
    setBusy(label);
    setError(null);
    setDone(null);
    try {
      const file = await action();
      setDone(`Descargado: ${file}`);
      if (planId) setState(await api.plans.state(planId));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "No se pudo exportar.");
    } finally {
      setBusy(null);
    }
  }

  if (!plan || !assessment || !state) return <div className="page wrap"><p className="muted">{error ?? "Cargando…"}</p></div>;

  const validation = state.validationResults;
  const errorCount = validation.filter((v) => v.severity === "Error").length;
  const reviewCount = validation.filter((v) => v.severity === "Review").length;
  const canExport = errorCount === 0;
  const needsApprovedBy = plan.isCurricularChange || reviewCount > 0;
  const blocked = !canExport || (needsApprovedBy && !approvedBy.trim()) || busy !== null;
  const bundleNeeded = options.accessibleHtml || options.teacherSheet || options.answerKey || options.changeLog;

  const links = [
    "/upload", `/assessments/${assessment.id}/analysis`, `/assessments/${assessment.id}/adapt`,
    `/plans/${planId}`, `/plans/${planId}/edit`, undefined,
  ];

  return (
    <div className="page">
      <div className="wrap-narrow" style={{ maxWidth: 820 }}>
        <Eyebrow>Exportar</Eyebrow>
        <Stepper steps={PIPELINE_STEPS} current={5} links={links} />
        <h1 style={{ fontSize: 28, marginBottom: 4 }}>{assessment.title}</h1>
        <p className="muted" style={{ marginBottom: 24 }}>
          Última comprobación antes de generar el documento final. Se valida otra vez el documento tal como lo has dejado en el editor.
        </p>

        <div className="card-panel" style={{ marginBottom: 24 }} data-testid="export-summary">
          <div className="row spread" style={{ marginBottom: 12 }}>
            <div className="row">
              <SemaphoreBadge level={state.semaphore.overall} />
              <strong>{errorCount === 0 ? "Sin errores" : `${errorCount} error(es) de validación`}</strong>
            </div>
            <Link to={`/plans/${planId}/history`} className="btn btn-sm btn-outline">Historial de cambios</Link>
          </div>
          {validation.length === 0 && <p className="muted" style={{ fontSize: 13 }}>✓ La adaptación supera todas las comprobaciones.</p>}
          {validation.map((v) => (
            <p key={v.id} style={{ fontSize: 13, margin: "0 0 6px" }}>
              <SeverityBadge severity={v.severity} /> <strong>{VALIDATION_LABELS[v.code] ?? v.code}</strong> — {v.message}
            </p>
          ))}
          <p className="muted" style={{ fontSize: 13, marginTop: 10 }}>
            {canExport
              ? "El plan supera las comprobaciones de seguridad pedagógica y se puede exportar."
              : <>Hay errores sin resolver. Vuelve al <Link to={`/plans/${planId}`}>comparador</Link> o al <Link to={`/plans/${planId}/edit`}>editor</Link> para corregirlos.</>}
          </p>
        </div>

        <div className="card-panel">
          <h3 style={{ fontSize: 16, marginBottom: 12 }}>Descargar</h3>
          <div className="field" style={{ maxWidth: 340 }}>
            <label htmlFor="approved-by">Aprobado por (docente){needsApprovedBy ? " · obligatorio" : ""}</label>
            <input id="approved-by" value={approvedBy} onChange={(e) => setApprovedBy(e.target.value)} placeholder="Nombre o alias del docente" />
            {needsApprovedBy && <span className="field-hint">Hay puntos para revisar{plan.isCurricularChange ? " y una adaptación curricular" : ""}: tu aprobación queda registrada en el historial.</span>}
          </div>

          <div className="row" style={{ marginBottom: 18 }}>
            <button className="btn" disabled={blocked} onClick={() => run("pdf", () => api.plans.export(planId!, "Pdf", approvedBy))}>
              {busy === "pdf" ? "Generando…" : "Descargar PDF"}
            </button>
            <button className="btn btn-outline" disabled={blocked} onClick={() => run("docx", () => api.plans.export(planId!, "Docx", approvedBy))}>
              {busy === "docx" ? "Generando…" : "Descargar Word"}
            </button>
          </div>

          <details open={bundleNeeded}>
            <summary style={{ cursor: "pointer", fontWeight: 600, color: "var(--text)", marginBottom: 12 }}>Paquete con extras (ZIP)</summary>
            <ExportOptions value={options} onChange={setOptions} />
            <button
              className="btn btn-outline" style={{ marginTop: 16 }} disabled={blocked || (!options.pdf && !options.docx && !options.accessibleHtml)}
              onClick={() => run("zip", () => api.plans.exportBundle(planId!, { ...options, approvedBy }))}
            >
              {busy === "zip" ? "Generando…" : "Descargar paquete ZIP"}
            </button>
          </details>

          {done && <div style={{ marginTop: 14 }}><Callout kind="success">{done}</Callout></div>}
          {error && <div style={{ marginTop: 14 }}><Callout kind="error">{error}</Callout></div>}
        </div>
      </div>
    </div>
  );
}
