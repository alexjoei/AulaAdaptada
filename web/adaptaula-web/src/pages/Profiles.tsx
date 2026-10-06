import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { NecessityPreset, StudentProfile } from "../api/types";
import { Eyebrow, EmptyState, Callout } from "../components/ui";
import ProfileEditor from "../components/ProfileEditor";
import { useLibrary } from "../hooks/useLibrary";

export default function Profiles() {
  const { library } = useLibrary();
  const [profiles, setProfiles] = useState<StudentProfile[]>([]);
  const [editing, setEditing] = useState<StudentProfile | "new" | null>(null);
  const [error, setError] = useState<string | null>(null);

  function refresh() {
    api.profiles.list().then(setProfiles).catch(() => setError("No se pudieron cargar los perfiles."));
  }

  useEffect(refresh, []);

  const needName = new Map<string, NecessityPreset>((library?.needs ?? []).map((n) => [n.key, n]));

  async function remove(p: StudentProfile) {
    if (!window.confirm(`¿Eliminar a «${p.alias}»? También se borrarán las adaptaciones hechas para este alumno.`)) return;
    await api.profiles.remove(p.id);
    refresh();
  }

  async function duplicate(p: StudentProfile) {
    const alias = window.prompt("Alias del nuevo perfil (parte de las mismas medidas):", `${p.alias} (copia)`);
    if (!alias) return;
    try {
      await api.profiles.duplicate(p.id, alias);
      refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo duplicar el perfil.");
    }
  }

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Perfiles</Eyebrow>
        <h1 style={{ fontSize: 28, marginBottom: 8 }}>Perfiles de alumnado</h1>
        <p className="muted" style={{ marginBottom: 20, maxWidth: 680 }}>
          Un perfil guarda las medidas reales de un alumno: tamaño de letra, tiempo extra, fragmentación, tipo de respuesta… Dos alumnos con la
          misma necesidad pueden tener perfiles distintos.
        </p>
        <div style={{ marginBottom: 24, maxWidth: 680 }}>
          <Callout kind="info">
            <strong>Privacidad:</strong> guarda solo un alias («Alumno A»), nunca nombres reales ni diagnósticos clínicos. Los perfiles, las pruebas y las
            adaptaciones son información educativa sensible: elimínalos cuando dejen de hacer falta.
          </Callout>
        </div>
        {error && <p style={{ color: "var(--error)", marginBottom: 12 }} role="alert">{error}</p>}

        {editing === null && (
          <div className="row" style={{ marginBottom: 24 }}>
            <button className="btn" onClick={() => setEditing("new")}>Nuevo perfil</button>
          </div>
        )}

        {editing !== null && (
          <div className="card-panel" style={{ marginBottom: 28 }}>
            <h3 style={{ fontSize: 18, marginBottom: 14 }}>{editing === "new" ? "Nuevo perfil" : `Editar «${editing.alias}»`}</h3>
            <ProfileEditor
              profile={editing === "new" ? undefined : editing}
              onSaved={() => { setEditing(null); refresh(); }}
              onCancel={() => setEditing(null)}
            />
          </div>
        )}

        {profiles.length === 0 && editing === null
          ? <EmptyState title="Todavía no hay perfiles" description="Crea uno para reutilizar sus medidas en próximas adaptaciones." />
          : (
            <div className="card-grid cols-3">
              {profiles.map((p) => (
                <div key={p.id} className="card-panel" data-profile={p.alias}>
                  <div className="row spread" style={{ marginBottom: 10 }}>
                    <strong>{p.alias}</strong>
                    <span className="muted" style={{ fontSize: 12 }}>
                      {p.accommodations.length} añadidas · {p.exceptions.length} desactivadas
                    </span>
                  </div>
                  <div className="row" style={{ marginBottom: 14 }}>
                    {p.measures.map((m) => <span key={m} className="badge">{needName.get(m)?.displayName ?? m}</span>)}
                    {p.measures.length === 0 && <span className="muted" style={{ fontSize: 12 }}>Sin necesidades predefinidas</span>}
                  </div>
                  <div className="row">
                    <button className="btn-sm btn btn-outline" onClick={() => setEditing(p)}>Editar</button>
                    <button className="btn-sm btn btn-outline" onClick={() => duplicate(p)}>Duplicar</button>
                    <button className="btn-sm btn btn-outline" onClick={() => remove(p)}>Eliminar</button>
                  </div>
                </div>
              ))}
            </div>
          )}
      </div>
    </div>
  );
}
