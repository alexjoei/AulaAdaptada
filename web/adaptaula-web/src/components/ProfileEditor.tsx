import { useState } from "react";
import { api } from "../api/client";
import type { StudentProfile } from "../api/types";
import { useLibrary } from "../hooks/useLibrary";
import MeasurePicker, { type MeasureSelection } from "./MeasurePicker";
import { Callout } from "./ui";

interface Props {
  /** Editing an existing profile; omit to create a new one. */
  profile?: StudentProfile;
  onSaved: (profile: StudentProfile) => void;
  onCancel?: () => void;
}

/** Heuristic only: two capitalised words ("María Pérez") look like a real name. Generic aliases ("Alumno A", "alu-14") are fine. */
export function looksLikeRealName(alias: string) {
  const trimmed = alias.trim();
  if (/^(alumn[oa]|alu|est|student)\b/i.test(trimmed)) return false;
  return /^\p{Lu}\p{Ll}{2,}\s+\p{Lu}\p{Ll}{2,}/u.test(trimmed);
}

export default function ProfileEditor({ profile, onSaved, onCancel }: Props) {
  const { library, error, reload } = useLibrary();
  const [alias, setAlias] = useState(profile?.alias ?? "");
  const [needs, setNeeds] = useState<string[]>(profile?.measures ?? []);
  const [selection, setSelection] = useState<MeasureSelection>({
    accommodations: profile?.accommodations ?? [],
    exceptions: profile?.exceptions ?? [],
    settings: profile?.settings ?? {},
  });
  const [busy, setBusy] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  if (error) return <Callout kind="error">{error}</Callout>;
  if (!library) return <p className="muted">Cargando biblioteca de medidas…</p>;

  const visibleNeeds = library.needs.filter((n) => !n.hidden);

  function toggleNeed(key: string) {
    setNeeds((prev) => (prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]));
  }

  async function save() {
    setBusy(true);
    setSaveError(null);
    try {
      const body = { alias: alias.trim(), measures: needs, ...selection };
      const saved = profile ? await api.profiles.update(profile.id, body) : await api.profiles.create(body);
      onSaved(saved);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : "No se pudo guardar el perfil.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div data-testid="profile-editor">
      <div className="field" style={{ maxWidth: 360 }}>
        <label htmlFor="profile-alias">Alias del alumno</label>
        <input id="profile-alias" value={alias} maxLength={40} onChange={(e) => setAlias(e.target.value)} placeholder="Alumno A" />
        <span className="field-hint">Solo un alias o código. Nunca el nombre real ni el diagnóstico clínico.</span>
      </div>
      {looksLikeRealName(alias) && (
        <div style={{ marginBottom: 16 }}>
          <Callout kind="warning">Ese alias parece un nombre real. Usa un código («Alumno A», «alu-14») para proteger su privacidad.</Callout>
        </div>
      )}

      <div className="field">
        <label>Necesidades (puedes combinar varias)</label>
        <div className="row" role="group" aria-label="Necesidades">
          {visibleNeeds.map((n) => (
            <button
              type="button" key={n.key} title={n.description} aria-pressed={needs.includes(n.key)}
              className={`tag-chip${needs.includes(n.key) ? " selected" : ""}`} onClick={() => toggleNeed(n.key)}
              data-need={n.key}
            >
              {n.displayName}
            </button>
          ))}
        </div>
        <span className="field-hint">
          Una necesidad solo es un punto de partida: lo que se aplica de verdad son las medidas de abajo, que puedes activar, desactivar y ajustar
          para cada alumno. Dos alumnos con la misma necesidad pueden tener configuraciones distintas.
        </span>
      </div>

      <h3 style={{ fontSize: 16, margin: "20px 0 10px" }}>Medidas</h3>
      <MeasurePicker
        library={library} needs={needs} value={selection} schemaVersion={profile ? profile.schemaVersion : 2}
        onChange={setSelection} onLibraryChanged={reload}
      />

      {saveError && <p style={{ color: "var(--error)", margin: "12px 0" }} role="alert">{saveError}</p>}
      <div className="row" style={{ marginTop: 20 }}>
        <button className="btn" disabled={!alias.trim() || busy} onClick={save}>{profile ? "Guardar cambios" : "Crear perfil"}</button>
        {onCancel && <button className="btn btn-outline" onClick={onCancel}>Cancelar</button>}
      </div>
    </div>
  );
}
