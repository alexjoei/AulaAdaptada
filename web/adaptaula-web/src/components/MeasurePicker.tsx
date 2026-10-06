import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { EffectiveMeasure, Measure, MeasureLibrary } from "../api/types";
import { ClassificationBadge } from "./ui";

export interface MeasureSelection {
  accommodations: string[];
  exceptions: string[];
  settings: Record<string, string>;
}

interface Props {
  library: MeasureLibrary;
  needs: string[];
  value: MeasureSelection;
  schemaVersion?: number;
  onChange: (value: MeasureSelection) => void;
  onLibraryChanged: () => void;
}

/** The kind of effect a measure has, in teacher language. */
const KIND_LABEL: Record<string, string> = {
  Text: "Reescribe el texto",
  Support: "Añade un apoyo",
  Style: "Cambia la presentación",
  Logistics: "Se aplica en el aula",
};

/**
 * The measures behind a set of needs (V2 §2-4, §18): every measure grouped by area, each one switchable, classified as
 * Recommended / Optional / Requires teacher decision / Not recommended, with its own per-student parameters. A measure the
 * teacher turns on or off here is an individual choice and always beats the preset.
 */
export default function MeasurePicker({ library, needs, value, schemaVersion = 2, onChange, onLibraryChanged }: Props) {
  const [effective, setEffective] = useState<EffectiveMeasure[]>([]);
  const [onlyActive, setOnlyActive] = useState(false);
  const [addId, setAddId] = useState("");
  const [custom, setCustom] = useState({ description: "", group: "presentation", kind: "Text", alters: true });
  const [customError, setCustomError] = useState<string | null>(null);
  const [showCustom, setShowCustom] = useState(false);

  const measureById = useMemo(() => new Map<string, Measure>(library.measures.map((m) => [m.id, m])), [library]);

  useEffect(() => {
    let cancelled = false;
    api.profiles
      .effective({ needs, accommodations: value.accommodations, exceptions: value.exceptions, schemaVersion })
      .then((list) => { if (!cancelled) setEffective(list); })
      .catch(() => { if (!cancelled) setEffective([]); });
    return () => { cancelled = true; };
  }, [needs.join("|"), value.accommodations.join("|"), value.exceptions.join("|"), schemaVersion]); // eslint-disable-line react-hooks/exhaustive-deps

  /** Whether the need alone would switch this measure on (so toggling it must go through "exceptions", not "accommodations"). */
  function defaultOn(m: EffectiveMeasure) {
    return schemaVersion < 2 ? m.classification !== "NotRecommended" : m.classification === "Recommended" && !m.classificationConflict;
  }

  /** Computed here (not read back from the server) so a click is reflected instantly instead of flickering until the request returns. */
  function isEnabled(m: EffectiveMeasure) {
    return (value.accommodations.includes(m.ruleId) || defaultOn(m)) && !value.exceptions.includes(m.ruleId);
  }

  function toggle(m: EffectiveMeasure, on: boolean) {
    const acc = new Set(value.accommodations);
    const exc = new Set(value.exceptions);
    if (on) {
      exc.delete(m.ruleId);
      if (!defaultOn(m)) acc.add(m.ruleId);
    } else {
      acc.delete(m.ruleId);
      if (defaultOn(m)) exc.add(m.ruleId);
    }
    onChange({ ...value, accommodations: [...acc], exceptions: [...exc] });
  }

  function setParam(ruleId: string, key: string, v: string) {
    const settings = { ...value.settings };
    if (v === "") delete settings[`${ruleId}.${key}`];
    else settings[`${ruleId}.${key}`] = v;
    onChange({ ...value, settings });
  }

  const effectiveIds = new Set(effective.map((e) => e.ruleId));
  const addable = library.measures.filter((m) => !effectiveIds.has(m.id));

  async function addMeasure() {
    if (!addId) return;
    onChange({ ...value, accommodations: [...new Set([...value.accommodations, addId])] });
    setAddId("");
  }

  async function createCustom() {
    setCustomError(null);
    try {
      const created = await api.measures.createCustom({
        description: custom.description, group: custom.group, kind: custom.kind as Measure["kind"], altersAssessedConstruct: custom.alters,
      });
      onLibraryChanged();
      onChange({ ...value, accommodations: [...new Set([...value.accommodations, created.id])] });
      setCustom({ description: "", group: "presentation", kind: "Text", alters: true });
      setShowCustom(false);
    } catch (e) {
      setCustomError(e instanceof Error ? e.message : "No se pudo crear la medida.");
    }
  }

  const rows = effective
    .map((e) => ({ e, m: measureById.get(e.ruleId) }))
    .filter((x): x is { e: EffectiveMeasure; m: Measure } => x.m !== undefined)
    .filter((x) => !onlyActive || isEnabled(x.e));

  const enabledCount = effective.filter(isEnabled).length;

  return (
    <div data-testid="measure-picker">
      <div className="row spread" style={{ marginBottom: 12 }}>
        <p className="muted" style={{ fontSize: 13 }}>
          <strong data-testid="enabled-count">{enabledCount}</strong> medidas activas de {effective.length} disponibles para estas necesidades.
          Las <em>recomendadas</em> se activan solas; el resto las decides tú.
        </p>
        <label className="checkbox-row">
          <input type="checkbox" checked={onlyActive} onChange={(e) => setOnlyActive(e.target.checked)} />
          Ver solo las activas
        </label>
      </div>

      {library.groups.map((group) => {
        const inGroup = rows.filter((x) => x.m.group.toLowerCase() === group.key);
        if (inGroup.length === 0) return null;
        return (
          <section key={group.key} className="measure-group" aria-label={group.label}>
            <div className="measure-group-title">{group.label}</div>
            {inGroup.map(({ e, m }) => (
              <div key={m.id} className={`measure-row${isEnabled(e) ? " on" : ""}`} data-measure={m.id}>
                <input
                  type="checkbox" checked={isEnabled(e)} aria-label={m.description}
                  onChange={(ev) => toggle(e, ev.target.checked)}
                />
                <div>
                  <div className="measure-desc">{m.description}{m.isCustom && <span className="badge" style={{ marginLeft: 8 }}>Mía</span>}</div>
                  {m.rationale && <div className="measure-why">{m.rationale}</div>}
                </div>
                <div className="measure-meta">
                  <ClassificationBadge value={e.classification} />
                  <span className="badge">{KIND_LABEL[m.kind]}</span>
                  {m.altersAssessedConstruct && <span className="badge badge-warning" title="Puede cambiar lo que se evalúa: la IA solo lo propone">Solo como propuesta</span>}
                </div>
                {isEnabled(e) && m.parameters.length > 0 && (
                  <div className="measure-params">
                    {m.parameters.map((p) => (
                      <label key={p.key}>
                        {p.label}
                        <input
                          type="number" step="any" placeholder={p.default}
                          value={value.settings[`${m.id}.${p.key}`] ?? ""}
                          onChange={(ev) => setParam(m.id, p.key, ev.target.value)}
                          aria-label={`${m.description}: ${p.label}`}
                        />
                        {p.unit}
                      </label>
                    ))}
                  </div>
                )}
                {e.warning && <div className="measure-warning">⚠ {e.warning}</div>}
              </div>
            ))}
          </section>
        );
      })}

      {rows.length === 0 && (
        <p className="muted" style={{ fontSize: 13, margin: "8px 0 16px" }}>
          {effective.length === 0 ? "Elige una o varias necesidades arriba, o añade medidas sueltas de la biblioteca." : "No hay medidas activas todavía."}
        </p>
      )}

      <div className="row" style={{ marginTop: 8 }}>
        <select value={addId} onChange={(e) => setAddId(e.target.value)} aria-label="Añadir una medida de la biblioteca" style={{ maxWidth: 360 }}>
          <option value="">+ Añadir otra medida de la biblioteca…</option>
          {library.groups.map((g) => (
            <optgroup key={g.key} label={g.label}>
              {addable.filter((m) => m.group.toLowerCase() === g.key).map((m) => <option key={m.id} value={m.id}>{m.description}</option>)}
            </optgroup>
          ))}
        </select>
        <button type="button" className="btn btn-sm btn-outline" disabled={!addId} onClick={addMeasure}>Añadir</button>
        <button type="button" className="btn btn-sm btn-outline" onClick={() => setShowCustom((s) => !s)}>
          {showCustom ? "Cancelar medida propia" : "Crear medida propia"}
        </button>
      </div>

      {showCustom && (
        <div className="card-panel" style={{ marginTop: 12 }}>
          <div className="field">
            <label htmlFor="custom-desc">Qué debe hacer la medida</label>
            <textarea
              id="custom-desc" rows={2} value={custom.description} maxLength={400}
              onChange={(e) => setCustom({ ...custom, description: e.target.value })}
              placeholder="Ej.: escribir las fechas siempre con el mes en letra"
            />
          </div>
          <div className="row">
            <div className="field">
              <label htmlFor="custom-group">Grupo</label>
              <select id="custom-group" value={custom.group} onChange={(e) => setCustom({ ...custom, group: e.target.value })}>
                {library.groups.map((g) => <option key={g.key} value={g.key}>{g.label}</option>)}
              </select>
            </div>
            <div className="field">
              <label htmlFor="custom-kind">Cómo se aplica</label>
              <select id="custom-kind" value={custom.kind} onChange={(e) => setCustom({ ...custom, kind: e.target.value })}>
                <option value="Text">Reescribe el texto (IA)</option>
                <option value="Support">Añade un apoyo (IA)</option>
                <option value="Logistics">Se aplica en el aula (solo aviso al docente)</option>
              </select>
            </div>
          </div>
          <label className="checkbox-row" style={{ marginBottom: 12 }}>
            <input type="checkbox" checked={custom.alters} onChange={(e) => setCustom({ ...custom, alters: e.target.checked })} />
            Podría cambiar lo que se evalúa (la IA solo la propondrá, sin aplicarla)
          </label>
          {customError && <p style={{ color: "var(--error)", marginBottom: 8 }} role="alert">{customError}</p>}
          <button type="button" className="btn btn-sm" disabled={!custom.description.trim()} onClick={createCustom}>Guardar medida propia</button>
        </div>
      )}
    </div>
  );
}
