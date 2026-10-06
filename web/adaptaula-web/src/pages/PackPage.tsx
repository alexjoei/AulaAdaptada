import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { ExportBundleOptions, PackResponse } from "../api/types";
import { Callout, Eyebrow, SemaphoreBadge } from "../components/ui";
import ExportOptions, { DEFAULT_OPTIONS } from "../components/ExportOptions";

const STATUS_LABEL: Record<string, string> = {
  Draft: "Borrador", Planned: "Sin generar", Generated: "Generada", NeedsTeacherReview: "Por revisar", Approved: "Aprobada", Exported: "Exportada",
};

/** Class pack (V2 §19): Original + one version per student, generated together and reviewed one by one before exporting. */
export default function PackPage() {
  const { id: packId } = useParams<{ id: string }>();
  const [pack, setPack] = useState<PackResponse | null>(null);
  const [options, setOptions] = useState<ExportBundleOptions>(DEFAULT_OPTIONS);
  const [approvedBy, setApprovedBy] = useState("");
  const [progress, setProgress] = useState<{ current: number; total: number; alias: string } | null>(null);
  const [exporting, setExporting] = useState(false);
  const [done, setDone] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reasons, setReasons] = useState<string[]>([]);

  async function refresh() {
    if (!packId) return;
    setPack(await api.packs.get(packId));
  }

  useEffect(() => { refresh().catch(() => setError("No se pudo cargar el pack.")); }, [packId]); // eslint-disable-line react-hooks/exhaustive-deps

  async function generateAll() {
    if (!pack) return;
    const pending = pack.plans.filter((p) => !p.generated);
    setError(null);
    for (let i = 0; i < pending.length; i++) {
      setProgress({ current: i + 1, total: pending.length, alias: pending[i].alias });
      try {
        await api.plans.generate(pending[i].planId);
      } catch (e) {
        setError(`No se pudo generar la versión de ${pending[i].alias}: ${e instanceof Error ? e.message : "error desconocido"}`);
        break;
      }
      await refresh();
    }
    setProgress(null);
    await refresh();
  }

  async function exportPack() {
    if (!packId) return;
    setExporting(true);
    setError(null);
    setReasons([]);
    setDone(null);
    try {
      const file = await api.packs.export(packId, { ...options, approvedBy });
      setDone(`Descargado: ${file}`);
      await refresh();
    } catch (e) {
      if (e instanceof ApiError) { setError(e.message); setReasons(e.reasons ?? []); }
      else setError("No se pudo exportar el pack.");
    } finally {
      setExporting(false);
    }
  }

  if (!pack) return <div className="page wrap"><p className="muted">{error ?? "Cargando…"}</p></div>;

  const allGenerated = pack.plans.every((p) => p.generated);
  const anyReview = pack.plans.some((p) => p.reviews > 0 || p.pendingProposals > 0);

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Pack de aula</Eyebrow>
        <h1 style={{ fontSize: 28, marginBottom: 4 }}>{pack.title}</h1>
        <p className="muted" style={{ marginBottom: 24, maxWidth: 680 }}>
          Original + una versión por alumno. Genera todas de una vez, revisa cada versión (comparador y editor) y exporta el pack completo con su hoja docente.
          <br /><Link to={`/assessments/${pack.assessmentId}/analysis`}>← Volver a la prueba</Link>
        </p>

        <div className="card-panel" style={{ marginBottom: 24 }}>
          <div className="row spread" style={{ marginBottom: 14 }}>
            <h3 style={{ fontSize: 16 }}>Versiones</h3>
            {!allGenerated && (
              <button className="btn btn-sm" disabled={progress !== null} onClick={generateAll}>
                {progress ? `Generando ${progress.current}/${progress.total} · ${progress.alias}…` : "Generar todas las versiones"}
              </button>
            )}
          </div>

          {progress && (
            <div className="progress-bar-track" style={{ marginBottom: 14 }} role="progressbar" aria-valuenow={progress.current - 1} aria-valuemax={progress.total}>
              <div className="progress-bar-fill" style={{ width: `${((progress.current - 1) / progress.total) * 100}%` }} />
            </div>
          )}

          <table className="data-table" data-testid="pack-table">
            <thead>
              <tr><th>Alumno</th><th>Estado</th><th>Semáforo</th><th>Errores</th><th>Por revisar</th><th></th></tr>
            </thead>
            <tbody>
              <tr><td><strong>Original</strong></td><td colSpan={5} className="muted">La prueba tal cual, incluida en el paquete.</td></tr>
              {pack.plans.map((p) => (
                <tr key={p.planId} data-student={p.alias}>
                  <td><strong>{p.alias}</strong></td>
                  <td>{p.generated ? STATUS_LABEL[p.status] ?? p.status : "Sin generar"}</td>
                  <td>{p.semaphore ? <SemaphoreBadge level={p.semaphore} /> : <span className="muted">—</span>}</td>
                  <td>{p.generated ? p.errors : "—"}</td>
                  <td>{p.generated ? p.reviews + p.pendingProposals : "—"}</td>
                  <td>
                    <div className="row">
                      <Link className="btn btn-sm btn-outline" to={`/plans/${p.planId}`}>Revisar</Link>
                      {p.generated && <Link className="btn btn-sm btn-outline" to={`/plans/${p.planId}/edit`}>Editor</Link>}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="card-panel">
          <h3 style={{ fontSize: 16, marginBottom: 12 }}>Exportar pack</h3>
          {!allGenerated && <div style={{ marginBottom: 12 }}><Callout kind="info">Genera primero todas las versiones para poder exportar el pack.</Callout></div>}
          <ExportOptions value={options} onChange={setOptions} showOriginal />
          <div className="field" style={{ maxWidth: 340, marginTop: 16 }}>
            <label htmlFor="pack-approved">Aprobado por (docente){anyReview ? " · obligatorio" : ""}</label>
            <input id="pack-approved" value={approvedBy} onChange={(e) => setApprovedBy(e.target.value)} placeholder="Nombre o alias del docente" />
          </div>
          <button className="btn" disabled={!allGenerated || exporting || (anyReview && !approvedBy.trim())} onClick={exportPack}>
            {exporting ? "Generando paquete…" : "Descargar pack (ZIP)"}
          </button>

          {done && <div style={{ marginTop: 14 }}><Callout kind="success">{done}</Callout></div>}
          {error && (
            <div style={{ marginTop: 14 }}>
              <Callout kind="error">
                {error}
                {reasons.length > 0 && <ul style={{ margin: "8px 0 0", paddingLeft: 18 }}>{reasons.map((r, i) => <li key={i}>{r}</li>)}</ul>}
              </Callout>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
