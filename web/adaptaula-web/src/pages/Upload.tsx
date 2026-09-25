import { useState, type DragEvent } from "react";
import { useNavigate } from "react-router-dom";
import { api, ApiError } from "../api/client";
import { Eyebrow, Stepper, PIPELINE_STEPS } from "../components/ui";

export default function Upload() {
  const navigate = useNavigate();
  const [mode, setMode] = useState<"file" | "text">("file");
  const [file, setFile] = useState<File | null>(null);
  const [dragActive, setDragActive] = useState(false);
  const [title, setTitle] = useState("");
  const [text, setText] = useState("");
  const [grade, setGrade] = useState(6);
  const [subject, setSubject] = useState("");
  const [language, setLanguage] = useState("es");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const assessment = mode === "file" && file
        ? await api.assessments.upload(file, grade, subject, language)
        : await api.assessments.createFromText({ title, text, grade, subject, language });
      navigate(`/assessments/${assessment.id}/analysis`);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "No se pudo procesar el documento.");
    } finally {
      setBusy(false);
    }
  }

  function onDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setDragActive(false);
    const dropped = e.dataTransfer.files[0];
    if (dropped) setFile(dropped);
  }

  const canSubmit = mode === "file" ? !!file && !!subject : !!title && !!text && !!subject;

  return (
    <div className="page">
      <div className="wrap-narrow">
        <Eyebrow>Nueva adaptación</Eyebrow>
        <Stepper steps={PIPELINE_STEPS} current={0} />
        <h1 style={{ fontSize: 28, marginBottom: 8 }}>Sube una prueba</h1>
        <p className="muted" style={{ marginBottom: 28 }}>
          MVP1 admite DOCX, PDF con texto (no escaneado) o pegar el texto directamente.
        </p>

        <div className="card-panel">
          <div className="row" style={{ marginBottom: 20 }}>
            <button className={`btn-sm ${mode === "file" ? "btn" : "btn-outline"}`} onClick={() => setMode("file")}>Archivo</button>
            <button className={`btn-sm ${mode === "text" ? "btn" : "btn-outline"}`} onClick={() => setMode("text")}>Pegar texto</button>
          </div>

          {mode === "file" ? (
            <div
              className={`dropzone${dragActive ? " active" : ""}`}
              onDragOver={(e) => { e.preventDefault(); setDragActive(true); }}
              onDragLeave={() => setDragActive(false)}
              onDrop={onDrop}
              onClick={() => document.getElementById("file-input")?.click()}
            >
              <input
                id="file-input" type="file" accept=".docx,.pdf" style={{ display: "none" }}
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
              {file ? <p><strong>{file.name}</strong></p> : <p>Arrastra un archivo DOCX o PDF, o haz clic para elegirlo.</p>}
            </div>
          ) : (
            <div className="stack">
              <div className="field">
                <label>Título de la prueba</label>
                <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Nutrition Test" />
              </div>
              <div className="field">
                <label>Texto</label>
                <textarea rows={10} value={text} onChange={(e) => setText(e.target.value)} placeholder="Pega aquí el contenido de la prueba…" />
              </div>
            </div>
          )}

          <div className="card-grid cols-3 mt-24">
            <div className="field">
              <label>Curso</label>
              <input type="number" min={1} max={12} value={grade} onChange={(e) => setGrade(Number(e.target.value))} />
            </div>
            <div className="field">
              <label>Asignatura</label>
              <input value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="Natural Science" />
            </div>
            <div className="field">
              <label>Idioma</label>
              <select value={language} onChange={(e) => setLanguage(e.target.value)}>
                <option value="es">Español</option>
                <option value="en">English</option>
                <option value="ca">Català</option>
              </select>
            </div>
          </div>

          {error && <p style={{ color: "var(--error)", marginBottom: 12 }}>{error}</p>}

          <button className="btn" disabled={!canSubmit || busy} onClick={submit}>
            {busy ? "Procesando…" : "Analizar prueba"}
          </button>
        </div>
      </div>
    </div>
  );
}
