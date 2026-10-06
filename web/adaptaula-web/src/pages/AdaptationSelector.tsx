import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import type { Assessment, StudentProfile } from "../api/types";
import { Eyebrow, Stepper, PIPELINE_STEPS, Callout } from "../components/ui";
import ProfileEditor from "../components/ProfileEditor";
import CurricularPicker, { EMPTY_CURRICULAR, type CurricularChoice } from "../components/CurricularPicker";
import { useLibrary } from "../hooks/useLibrary";

const LEVELS = [
  { value: 1, label: "1 · Accesibilidad", hint: "Diseño y acceso: tipografía, espaciado, contraste, formato. No cambia contenido ni dificultad." },
  { value: 2, label: "2 · Andamiaje", hint: "Pasos, ejemplos, glosarios y ayudas que apoyan sin tocar el objetivo." },
];

interface StudentConfig {
  level: number;
  curricular: boolean;
  choice: CurricularChoice;
}

const DEFAULT_CONFIG: StudentConfig = { level: 2, curricular: false, choice: EMPTY_CURRICULAR };

export default function AdaptationSelector() {
  const { id: assessmentId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { library } = useLibrary();

  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [profiles, setProfiles] = useState<StudentProfile[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [configs, setConfigs] = useState<Record<string, StudentConfig>>({});
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!assessmentId) return;
    api.assessments.get(assessmentId).then(setAssessment).catch(() => setError("No se pudo cargar la prueba."));
    api.profiles.list().then(setProfiles).catch(() => setError("No se pudieron cargar los perfiles."));
  }, [assessmentId]);

  const needName = new Map((library?.needs ?? []).map((n) => [n.key, n.displayName]));

  function toggle(id: string) {
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
    setConfigs((prev) => (prev[id] ? prev : { ...prev, [id]: DEFAULT_CONFIG }));
  }

  function patch(id: string, change: Partial<StudentConfig>) {
    setConfigs((prev) => ({ ...prev, [id]: { ...(prev[id] ?? DEFAULT_CONFIG), ...change } }));
  }

  const missingReferents = selected.filter((id) => {
    const c = configs[id] ?? DEFAULT_CONFIG;
    return c.curricular && c.choice.criteriaIds.length === 0 && !c.choice.objective.trim();
  });

  async function create() {
    if (!assessmentId || selected.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      const items = selected.map((profileId) => {
        const c = configs[profileId] ?? DEFAULT_CONFIG;
        return {
          profileId,
          level: c.curricular ? 3 : c.level,
          curricularChangeAuthorized: c.curricular,
          curricularObjective: c.curricular ? c.choice.objective.trim() || undefined : undefined,
          curricularReference: c.curricular && c.choice.referenceGrade ? `${c.choice.referenceGrade}.º de Primaria` : undefined,
          curricularCriteriaIds: c.curricular ? c.choice.criteriaIds : undefined,
          curricularContentIds: c.curricular ? c.choice.contentIds : undefined,
        };
      });

      if (items.length === 1) {
        const { profileId, ...rest } = items[0];
        const plan = await api.plans.create(assessmentId, { profileId, ...rest });
        navigate(`/plans/${plan.id}`);
      } else {
        const pack = await api.packs.create(assessmentId, { title: assessment?.title, items });
        navigate(`/packs/${pack.id}`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo crear el plan de adaptación.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="page">
      <div className="wrap-narrow" style={{ maxWidth: 860 }}>
        <Eyebrow>Selector de adaptación</Eyebrow>
        <Stepper steps={PIPELINE_STEPS} current={2} links={["/upload", `/assessments/${assessmentId}/analysis`]} />
        <h1 style={{ fontSize: 28, marginBottom: 6 }}>¿Para quién es esta adaptación?</h1>
        <p className="muted" style={{ marginBottom: 20 }}>
          Elige uno o varios alumnos. Con uno se genera su adaptación; con varios, un <strong>pack de aula</strong>: original + una versión por
          alumno, cada una para revisar antes de exportar.
        </p>

        <div className="card-panel" style={{ marginBottom: 20 }}>
          <div className="row spread" style={{ marginBottom: 12 }}>
            <h3 style={{ fontSize: 16 }}>Alumnado</h3>
            {!creating && <button className="btn btn-sm btn-outline" onClick={() => setCreating(true)}>+ Perfil nuevo</button>}
          </div>

          {profiles.length === 0 && !creating && (
            <p className="muted" style={{ fontSize: 14 }}>Todavía no hay perfiles guardados. Crea el primero con «Perfil nuevo».</p>
          )}

          <div className="stack">
            {profiles.map((p) => (
              <label key={p.id} className="checkbox-row" style={{ alignItems: "flex-start" }} data-profile={p.alias}>
                <input type="checkbox" checked={selected.includes(p.id)} onChange={() => toggle(p.id)} style={{ marginTop: 3 }} />
                <span>
                  <strong style={{ color: "var(--text)" }}>{p.alias}</strong>{" "}
                  <span className="muted" style={{ fontSize: 13 }}>
                    {p.measures.map((m) => needName.get(m) ?? m).join(" + ") || "medidas individuales"}
                  </span>
                </span>
              </label>
            ))}
          </div>

          {creating && (
            <div style={{ marginTop: 18, paddingTop: 18, borderTop: "1px solid var(--border)" }}>
              <ProfileEditor
                onSaved={(profile) => {
                  setProfiles((prev) => [...prev, profile].sort((a, b) => a.alias.localeCompare(b.alias)));
                  toggle(profile.id);
                  setCreating(false);
                }}
                onCancel={() => setCreating(false)}
              />
            </div>
          )}
        </div>

        {selected.map((id) => {
          const profile = profiles.find((p) => p.id === id);
          const c = configs[id] ?? DEFAULT_CONFIG;
          if (!profile) return null;
          return (
            <div key={id} className="card-panel" style={{ marginBottom: 16 }} data-student-config={profile.alias}>
              <h3 style={{ fontSize: 16, marginBottom: 12 }}>{profile.alias}</h3>

              <div className="stack" style={{ marginBottom: 14 }}>
                {LEVELS.map((l) => (
                  <label key={l.value} className="checkbox-row" style={{ alignItems: "flex-start", opacity: c.curricular ? 0.5 : 1 }}>
                    <input
                      type="radio" name={`level-${id}`} checked={!c.curricular && c.level === l.value} disabled={c.curricular}
                      onChange={() => patch(id, { level: l.value })} style={{ marginTop: 3 }}
                    />
                    <span>
                      <strong style={{ display: "block", color: "var(--text)" }}>{l.label}</strong>
                      <span className="muted" style={{ fontSize: 13 }}>{l.hint}</span>
                    </span>
                  </label>
                ))}
              </div>

              <label className="checkbox-row" style={{ alignItems: "flex-start", padding: 12, border: "1px solid var(--border)", borderRadius: 14 }}>
                <input
                  type="checkbox" checked={c.curricular} aria-label={`¿Existe adaptación curricular? ${profile.alias}`}
                  onChange={(e) => patch(id, { curricular: e.target.checked })} style={{ marginTop: 3 }}
                />
                <span>
                  <strong style={{ display: "block", color: "var(--text)" }}>3 · ¿Existe adaptación curricular?</strong>
                  <span className="muted" style={{ fontSize: 13 }}>
                    Solo si tú la activas y defines los referentes. La IA nunca decide por sí sola bajar el nivel, y el currículo no cambia por un diagnóstico.
                  </span>
                </span>
              </label>

              {c.curricular && (
                <div style={{ marginTop: 14 }}>
                  <span className="badge badge-review" style={{ marginBottom: 12 }}>Adaptación curricular · aprobación obligatoria</span>
                  <CurricularPicker
                    value={c.choice} onChange={(choice) => patch(id, { choice })}
                    defaultCurriculumId={assessment?.curriculumId} defaultAreaId={assessment?.curriculumAreaId}
                  />
                </div>
              )}
            </div>
          );
        })}

        {missingReferents.length > 0 && (
          <div style={{ marginBottom: 14 }}>
            <Callout kind="warning">
              Has activado la adaptación curricular para {missingReferents.map((id) => profiles.find((p) => p.id === id)?.alias).join(", ")} pero no has
              elegido ningún criterio ni escrito un objetivo: sin referentes no se aplicará ningún cambio curricular.
            </Callout>
          </div>
        )}

        {error && <p style={{ color: "var(--error)", marginBottom: 12 }} role="alert">{error}</p>}

        <button className="btn" disabled={busy || selected.length === 0} onClick={create}>
          {busy ? "Creando…" : selected.length > 1 ? `Crear pack de aula (${selected.length} alumnos)` : "Generar adaptación"}
        </button>
      </div>
    </div>
  );
}
