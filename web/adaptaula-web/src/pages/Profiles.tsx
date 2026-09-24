import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { NecessityPreset, StudentProfile } from "../api/types";
import { Eyebrow, EmptyState } from "../components/ui";

export default function Profiles() {
  const [profiles, setProfiles] = useState<StudentProfile[]>([]);
  const [presets, setPresets] = useState<NecessityPreset[]>([]);
  const [alias, setAlias] = useState("");
  const [measures, setMeasures] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  function refresh() {
    api.profiles.list().then(setProfiles);
  }

  useEffect(() => {
    refresh();
    api.profiles.presets().then(setPresets);
  }, []);

  function toggleMeasure(key: string) {
    setMeasures((prev) => (prev.includes(key) ? prev.filter((m) => m !== key) : [...prev, key]));
  }

  async function create() {
    if (!alias) return;
    setBusy(true);
    try {
      await api.profiles.create({ alias, measures, accommodations: [], exceptions: [] });
      setAlias("");
      setMeasures([]);
      refresh();
    } finally {
      setBusy(false);
    }
  }

  async function remove(id: string) {
    await api.profiles.remove(id);
    refresh();
  }

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Perfiles</Eyebrow>
        <h1 style={{ fontSize: 28, marginBottom: 8 }}>Perfiles de alumnado</h1>
        <p className="muted" style={{ marginBottom: 28, maxWidth: 640 }}>
          Solo alias — nunca nombre real ni diagnóstico clínico. Un perfil guarda las medidas
          educativas configurables que se usarán al generar futuras adaptaciones.
        </p>

        <div className="card-panel" style={{ marginBottom: 28, maxWidth: 640 }}>
          <h3 style={{ fontSize: 16, marginBottom: 12 }}>Nuevo perfil</h3>
          <div className="field">
            <label>Alias</label>
            <input value={alias} onChange={(e) => setAlias(e.target.value)} placeholder="alu-14" />
          </div>
          <div className="field">
            <label>Medidas</label>
            <div className="row">
              {presets.map((p) => (
                <span key={p.key} className={`tag-chip${measures.includes(p.key) ? " selected" : ""}`} onClick={() => toggleMeasure(p.key)}>
                  {p.displayName}
                </span>
              ))}
            </div>
          </div>
          <button className="btn" disabled={!alias || busy} onClick={create}>Crear perfil</button>
        </div>

        {profiles.length === 0
          ? <EmptyState title="Todavía no hay perfiles" description="Crea uno para reutilizar sus medidas en próximas adaptaciones." />
          : (
            <div className="card-grid cols-3">
              {profiles.map((p) => (
                <div key={p.id} className="card-panel">
                  <div className="row spread" style={{ marginBottom: 10 }}>
                    <strong>{p.alias}</strong>
                    <button className="btn-sm btn-outline" onClick={() => remove(p.id)}>Eliminar</button>
                  </div>
                  <div className="row">
                    {p.measures.map((m) => <span key={m} className="badge">{m}</span>)}
                  </div>
                </div>
              ))}
            </div>
          )}
      </div>
    </div>
  );
}
