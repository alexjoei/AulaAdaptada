import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { Curriculum, CurriculumSummary } from "../api/types";
import { Callout } from "./ui";

export interface CurricularChoice {
  curriculumId: string;
  areaId: string;
  referenceGrade: number | null;
  criteriaIds: string[];
  contentIds: string[];
  objective: string;
}

export const EMPTY_CURRICULAR: CurricularChoice = {
  curriculumId: "", areaId: "", referenceGrade: null, criteriaIds: [], contentIds: [], objective: "",
};

interface Props {
  value: CurricularChoice;
  onChange: (v: CurricularChoice) => void;
  defaultCurriculumId?: string | null;
  defaultAreaId?: string | null;
}

/**
 * The protected curricular-adaptation path (V2 §14): the teacher picks the reference course, the criteria and the contents
 * that apply. Nothing is suggested or lowered automatically — the curriculum list is independent of any need.
 */
export default function CurricularPicker({ value, onChange, defaultCurriculumId, defaultAreaId }: Props) {
  const [summaries, setSummaries] = useState<CurriculumSummary[]>([]);
  const [curriculum, setCurriculum] = useState<Curriculum | null>(null);

  useEffect(() => { api.curriculum.list().then(setSummaries).catch(() => setSummaries([])); }, []);

  useEffect(() => {
    if (!value.curriculumId && defaultCurriculumId) {
      onChange({ ...value, curriculumId: defaultCurriculumId, areaId: defaultAreaId ?? value.areaId });
    }
  }, [defaultCurriculumId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!value.curriculumId) { setCurriculum(null); return; }
    api.curriculum.get(value.curriculumId).then(setCurriculum).catch(() => setCurriculum(null));
  }, [value.curriculumId]);

  const area = curriculum?.areas.find((a) => a.id === value.areaId) ?? null;
  const cycle = curriculum && value.referenceGrade ? curriculum.cycles.find((c) => c.grades.includes(value.referenceGrade!))?.id : undefined;

  const competences = useMemo(() => area?.competences ?? [], [area]);
  const criteria = useMemo(() => (area?.criteria ?? []).filter((c) => !cycle || c.cycle === cycle), [area, cycle]);
  const contents = useMemo(() => (area?.contents ?? []).filter((c) => !cycle || c.cycle === cycle), [area, cycle]);
  const blocks = [...new Set(contents.map((c) => c.block))];

  function toggle(list: string[], id: string) {
    return list.includes(id) ? list.filter((x) => x !== id) : [...list, id];
  }

  return (
    <div data-testid="curricular-picker" className="stack">
      {curriculum && !curriculum.verified && (
        <Callout kind="warning">
          <strong>Datos curriculares a verificar.</strong> {curriculum.note}
        </Callout>
      )}

      <div className="row">
        <div className="field" style={{ marginBottom: 0 }}>
          <label htmlFor="cur-curriculum">Currículo</label>
          <select id="cur-curriculum" value={value.curriculumId} onChange={(e) => onChange({ ...value, curriculumId: e.target.value, areaId: "", criteriaIds: [], contentIds: [] })}>
            <option value="">— Elige —</option>
            {summaries.map((s) => <option key={s.id} value={s.id}>{s.stage} · {s.ccaa}</option>)}
          </select>
        </div>
        <div className="field" style={{ marginBottom: 0 }}>
          <label htmlFor="cur-area">Área</label>
          <select id="cur-area" value={value.areaId} disabled={!curriculum} onChange={(e) => onChange({ ...value, areaId: e.target.value, criteriaIds: [], contentIds: [] })}>
            <option value="">— Elige —</option>
            {curriculum?.areas.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select>
        </div>
        <div className="field" style={{ marginBottom: 0 }}>
          <label htmlFor="cur-grade">Curso de referencia</label>
          <select
            id="cur-grade" value={value.referenceGrade ?? ""} disabled={!curriculum}
            onChange={(e) => onChange({ ...value, referenceGrade: e.target.value ? Number(e.target.value) : null, criteriaIds: [], contentIds: [] })}
          >
            <option value="">— Elige —</option>
            {[1, 2, 3, 4, 5, 6].map((g) => <option key={g} value={g}>{g}.º de Primaria</option>)}
          </select>
        </div>
      </div>

      {area && value.referenceGrade && (
        <>
          <div>
            <div className="measure-group-title">Criterios de evaluación aplicables ({criteria.length})</div>
            {competences.map((comp) => {
              const compCriteria = criteria.filter((c) => c.competenceId === comp.id);
              if (compCriteria.length === 0) return null;
              return (
                <div key={comp.id} style={{ marginBottom: 10 }}>
                  <div style={{ fontSize: 12, fontWeight: 600, color: "var(--text)" }}>Competencia específica {comp.number}</div>
                  {compCriteria.map((c) => (
                    <label key={c.id} className="checkbox-row" style={{ alignItems: "flex-start", margin: "4px 0" }}>
                      <input type="checkbox" checked={value.criteriaIds.includes(c.id)} onChange={() => onChange({ ...value, criteriaIds: toggle(value.criteriaIds, c.id) })} style={{ marginTop: 3 }} />
                      <span><code style={{ fontSize: 11 }}>{c.id}</code> {c.text}</span>
                    </label>
                  ))}
                </div>
              );
            })}
          </div>

          <div>
            <div className="measure-group-title">Contenidos / saberes básicos aplicables ({contents.length})</div>
            {blocks.map((block) => (
              <div key={block} style={{ marginBottom: 10 }}>
                <div style={{ fontSize: 12, fontWeight: 600, color: "var(--text)" }}>{block}</div>
                {contents.filter((c) => c.block === block).map((c) => (
                  <label key={c.id} className="checkbox-row" style={{ alignItems: "flex-start", margin: "4px 0" }}>
                    <input type="checkbox" checked={value.contentIds.includes(c.id)} onChange={() => onChange({ ...value, contentIds: toggle(value.contentIds, c.id) })} style={{ marginTop: 3 }} />
                    <span>{c.text}</span>
                  </label>
                ))}
              </div>
            ))}
          </div>
        </>
      )}

      <div className="field" style={{ marginBottom: 0 }}>
        <label htmlFor="cur-objective">Objetivo o referente en tus palabras (opcional si ya elegiste criterios)</label>
        <textarea
          id="cur-objective" rows={3} value={value.objective} onChange={(e) => onChange({ ...value, objective: e.target.value })}
          placeholder="Ej.: sumar y restar con llevadas hasta 100 usando material manipulativo."
        />
        <span className="field-hint">La herramienta nunca baja el nivel por sí sola: solo adapta a los referentes que tú definas aquí.</span>
      </div>
    </div>
  );
}
