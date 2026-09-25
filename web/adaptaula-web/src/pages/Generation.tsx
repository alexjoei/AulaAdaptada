import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AdaptationPlan, Assessment, ValidationResult } from "../api/types";
import { Eyebrow, Stepper, SeverityBadge, PIPELINE_STEPS } from "../components/ui";

export default function Generation() {
  const { id: planId } = useParams<{ id: string }>();
  const [plan, setPlan] = useState<AdaptationPlan | null>(null);
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [validation, setValidation] = useState<ValidationResult[]>([]);
  const [approvedBy, setApprovedBy] = useState("");
  const [exporting, setExporting] = useState<"Docx" | "Pdf" | null>(null);
  const [exportedFormat, setExportedFormat] = useState<"Docx" | "Pdf" | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!planId) return;
    api.plans.get(planId).then(async (p) => {
      setPlan(p);
      const [a, v] = await Promise.all([
        api.assessments.get(p.assessmentId),
        api.plans.validationResults(planId),
      ]);
      setAssessment(a);
      setValidation(v);
    });
  }, [planId]);

  async function doExport(format: "Docx" | "Pdf") {
    if (!planId) return;
    setExporting(format);
    setError(null);
    try {
      await api.plans.export(planId, format, approvedBy);
      setExportedFormat(format);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "No se pudo exportar.");
    } finally {
      setExporting(null);
    }
  }

  if (!plan || !assessment) return <div className="page wrap"><p className="muted">Cargando…</p></div>;

  const errorCount = validation.filter((v) => v.severity === "Error").length;
  const reviewCount = validation.filter((v) => v.severity === "Review").length;
  const canExport = errorCount === 0;
  const needsApprovedBy = plan.isCurricularChange || reviewCount > 0;

  const links = [
    "/upload",
    `/assessments/${assessment.id}/analysis`,
    `/assessments/${assessment.id}/adapt`,
    `/plans/${planId}`,
    undefined,
  ];

  return (
    <div className="page">
      <div className="wrap-narrow">
        <Eyebrow>Generación</Eyebrow>
        <Stepper steps={PIPELINE_STEPS} current={4} links={links} />
        <h1 style={{ fontSize: 28, marginBottom: 4 }}>{assessment.title}</h1>
        <p className="muted" style={{ marginBottom: 24 }}>
          Última comprobación antes de generar el documento final para el alumno.
        </p>

        <div className="card-panel" style={{ marginBottom: 24 }}>
          <div className="row spread" style={{ marginBottom: 12 }}>
            <strong>{errorCount === 0 ? "Sin errores" : `${errorCount} error(es) de validación`}</strong>
            <div className="row">{validation.map((v) => <SeverityBadge key={v.id} severity={v.severity} />)}</div>
          </div>
          <p className="muted" style={{ fontSize: 13 }}>
            {canExport
              ? "El plan supera las comprobaciones de seguridad pedagógica y se puede exportar."
              : "Hay errores sin resolver. Vuelve al comparador para aceptar o rechazar los cambios señalados."}
          </p>
        </div>

        <div className="card-panel">
          <h3 style={{ fontSize: 16, marginBottom: 12 }}>Exportar</h3>
          <div className="field" style={{ maxWidth: 320 }}>
            <label>Aprobado por (docente){needsApprovedBy ? " · obligatorio" : ""}</label>
            <input value={approvedBy} onChange={(e) => setApprovedBy(e.target.value)} placeholder="Nombre o alias del docente" />
          </div>
          <div className="row">
            <button className="btn" disabled={!canExport || (needsApprovedBy && !approvedBy) || !!exporting} onClick={() => doExport("Docx")}>
              {exporting === "Docx" ? "Generando…" : "Generar DOCX"}
            </button>
            <button className="btn btn-outline" disabled={!canExport || (needsApprovedBy && !approvedBy) || !!exporting} onClick={() => doExport("Pdf")}>
              {exporting === "Pdf" ? "Generando…" : "Generar PDF"}
            </button>
          </div>
          {exportedFormat && <p style={{ color: "var(--success)", marginTop: 12 }}>Documento {exportedFormat} generado y descargado.</p>}
          {error && <p style={{ color: "var(--error)", marginTop: 12 }}>{error}</p>}
        </div>
      </div>
    </div>
  );
}
