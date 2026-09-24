import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import type { NecessityPreset, StudentProfile } from "../api/types";
import { Eyebrow, Stepper } from "../components/ui";

const LEVELS = [
  { value: 1, label: "1 · Accesibilidad", hint: "Tipografía, espaciado, contraste. No cambia contenido ni dificultad." },
  { value: 2, label: "2 · Andamiaje", hint: "Añade fragmentación, ejemplos, glosario y checklist sin tocar el objetivo." },
  { value: 3, label: "3 · Personalizado", hint: "Solo lo que autorices explícitamente. Requiere revisión obligatoria." },
];

export default function AdaptationSelector() {
  const { id: assessmentId } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [presets, setPresets] = useState<NecessityPreset[]>([]);
  const [profiles, setProfiles] = useState<StudentProfile[]>([]);
  const [selectedProfileId, setSelectedProfileId] = useState<string>("");
  const [newAlias, setNewAlias] = useState("");
  const [selectedMeasures, setSelectedMeasures] = useState<string[]>([]);
  const [level, setLevel] = useState(2);
  const [curricularAuthorized, setCurricularAuthorized] = useState(false);
  const [curricularObjective, setCurricularObjective] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.profiles.presets().then(setPresets);
    api.profiles.list().then(setProfiles);
  }, []);

  function toggleMeasure(key: string) {
    setSelectedMeasures((prev) => (prev.includes(key) ? prev.filter((m) => m !== key) : [...prev, key]));
  }

  async function createPlan() {
    if (!assessmentId) return;
    setBusy(true);
    setError(null);
    try {
      let profileId = selectedProfileId;
      if (!profileId) {
        if (!newAlias) { setError("Indica un alias para el alumno o selecciona un perfil guardado."); setBusy(false); return; }
        const created = await api.profiles.create({ alias: newAlias, measures: selectedMeasures, accommodations: [], exceptions: [] });
        profileId = created.id;
      }

      const plan = await api.plans.create(assessmentId, {
        profileId,
        level,
        curricularChangeAuthorized: curricularAuthorized,
        curricularObjective: curricularAuthorized ? curricularObjective : undefined,
      });
      navigate(`/plans/${plan.id}`);
    } catch {
      setError("No se pudo crear el plan de adaptación.");
    } finally {
      setBusy(false);
    }
  }

  const usingExistingProfile = !!selectedProfileId;

  return (
    <div className="page">
      <div className="wrap-narrow">
        <Eyebrow>Selector de adaptación</Eyebrow>
        <Stepper steps={["Subida", "Análisis", "Adaptación", "Comparador"]} current={2} />
        <h1 style={{ fontSize: 28, marginBottom: 20 }}>¿Para quién es esta adaptación?</h1>

        <div className="card-panel" style={{ marginBottom: 20 }}>
          <h3 style={{ fontSize: 16, marginBottom: 12 }}>Alumno</h3>
          {profiles.length > 0 && (
            <div className="field">
              <label>Perfil guardado</label>
              <select value={selectedProfileId} onChange={(e) => setSelectedProfileId(e.target.value)}>
                <option value="">— Crear uno nuevo —</option>
                {profiles.map((p) => <option key={p.id} value={p.id}>{p.alias}</option>)}
              </select>
            </div>
          )}
          {!usingExistingProfile && (
            <div className="field">
              <label>Alias del alumno (nunca su nombre real ni diagnóstico)</label>
              <input value={newAlias} onChange={(e) => setNewAlias(e.target.value)} placeholder="alu-14" />
            </div>
          )}
        </div>

        {!usingExistingProfile && (
          <div className="card-panel" style={{ marginBottom: 20 }}>
            <h3 style={{ fontSize: 16, marginBottom: 4 }}>Necesidades / medidas</h3>
            <p className="muted" style={{ fontSize: 13, marginBottom: 12 }}>
              Son medidas educativas configurables, no un diagnóstico. Puedes combinar varias.
            </p>
            <div className="row">
              {presets.map((p) => (
                <span
                  key={p.key}
                  className={`tag-chip${selectedMeasures.includes(p.key) ? " selected" : ""}`}
                  onClick={() => toggleMeasure(p.key)}
                >
                  {p.displayName}
                </span>
              ))}
            </div>
          </div>
        )}

        <div className="card-panel" style={{ marginBottom: 20 }}>
          <h3 style={{ fontSize: 16, marginBottom: 12 }}>Nivel de adaptación</h3>
          <div className="stack">
            {LEVELS.map((l) => (
              <label key={l.value} className="checkbox-row" style={{ alignItems: "flex-start" }}>
                <input type="radio" name="level" checked={level === l.value} onChange={() => setLevel(l.value)} style={{ marginTop: 3 }} />
                <span>
                  <strong style={{ display: "block", color: "var(--text)" }}>{l.label}</strong>
                  <span className="muted" style={{ fontSize: 13 }}>{l.hint}</span>
                </span>
              </label>
            ))}
          </div>
        </div>

        {level === 3 && (
          <div className="card-panel" style={{ marginBottom: 20, borderColor: "var(--review)" }}>
            <span className="badge badge-review" style={{ marginBottom: 12 }}>Cambio curricular · requiere aprobación</span>
            <label className="checkbox-row" style={{ marginBottom: 12 }}>
              <input type="checkbox" checked={curricularAuthorized} onChange={(e) => setCurricularAuthorized(e.target.checked)} />
              Autorizo explícitamente un cambio curricular para este alumno
            </label>
            {curricularAuthorized && (
              <div className="field">
                <label>Objetivo/referente curricular definido por el docente</label>
                <textarea
                  rows={3} value={curricularObjective} onChange={(e) => setCurricularObjective(e.target.value)}
                  placeholder="Ej: reconocer que los alimentos aportan diferentes nutrientes y relacionar 3 grupos básicos con una función."
                />
                <p className="field-hint">La herramienta nunca infiere esto por sí misma a partir de un perfil.</p>
              </div>
            )}
          </div>
        )}

        {error && <p style={{ color: "var(--error)", marginBottom: 12 }}>{error}</p>}

        <button className="btn" disabled={busy} onClick={createPlan}>
          {busy ? "Creando plan…" : "Generar adaptación"}
        </button>
      </div>
    </div>
  );
}
