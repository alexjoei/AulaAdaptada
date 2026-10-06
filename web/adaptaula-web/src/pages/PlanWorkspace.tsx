import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import type {
  AdaptationPlan, Assessment, AdaptedQuestion, ChangeLogEntry, GenerationProgress, Measure, PlanState, Question,
  QuestionSemaphore, ValidationResult,
} from "../api/types";
import { Callout, Eyebrow, SemaphoreBadge, SeverityBadge, Stepper, PIPELINE_STEPS } from "../components/ui";
import { buildSegments, hunksOf, undoHunk, type Hunk, type Segment } from "../utils/hunks";

interface SelectedHunk {
  adaptedQuestionId: string;
  hunkId: number;
}

/** Which measure caused a change: the AI's change log points at the fragment it touched; a hunk matches when it overlaps that fragment. */
function explainHunk(hunk: Hunk, changeLog: ChangeLogEntry[]): ChangeLogEntry | null {
  const norm = (s: string) => s.replace(/\*\*/g, "").replace(/\s+/g, " ").trim().toLowerCase();
  const added = norm(hunk.added);
  const removed = norm(hunk.removed);
  for (const entry of changeLog) {
    const after = norm(entry.after);
    const before = norm(entry.before);
    if (after && added && (after.includes(added) || added.includes(after))) return entry;
    if (before && removed && (before.includes(removed) || removed.includes(before))) return entry;
  }
  return null;
}

/** The AI marks keywords/action verbs with **double asterisks**; show them as bold, the way the exported document will. */
function Bold({ text }: { text: string }) {
  return (
    <>
      {text.split(/(\*\*[^*]+\*\*)/g).map((part, i) =>
        part.startsWith("**") && part.endsWith("**") && part.length > 4 ? <strong key={i}>{part.slice(2, -2)}</strong> : <span key={i}>{part}</span>)}
    </>
  );
}

function HunkText({
  segments, side, selected, onSelect, adaptedQuestionId,
}: {
  segments: Segment[]; side: "original" | "adapted"; selected: SelectedHunk | null; adaptedQuestionId: string;
  onSelect: (h: SelectedHunk | null) => void;
}) {
  return (
    <p style={{ marginTop: 8, whiteSpace: "pre-wrap" }}>
      {segments.map((s, i) => {
        if (s.kind === "same") return <Bold key={i} text={s.text} />;
        const text = side === "original" ? s.hunk.removed : s.hunk.added;
        if (!text.trim()) return null;
        const isSelected = selected?.adaptedQuestionId === adaptedQuestionId && selected.hunkId === s.hunk.id;
        return (
          <span
            key={i} role="button" tabIndex={0}
            className={`hunk${side === "original" ? " hunk-removed" : ""}`} data-hunk={s.hunk.id}
            style={isSelected ? { outline: "2px solid var(--grad-from)" } : undefined}
            onClick={() => onSelect(isSelected ? null : { adaptedQuestionId, hunkId: s.hunk.id })}
            onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSelect(isSelected ? null : { adaptedQuestionId, hunkId: s.hunk.id }); } }}
            title="Pulsa para ver qué medida lo provocó y por qué"
          >
            <Bold text={text} />
          </span>
        );
      })}
    </p>
  );
}

export default function PlanWorkspace() {
  const { id: planId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [plan, setPlan] = useState<AdaptationPlan | null>(null);
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [state, setState] = useState<PlanState | null>(null);
  const [measures, setMeasures] = useState<Record<string, Measure>>({});
  const [alias, setAlias] = useState("");
  const [generating, setGenerating] = useState(false);
  const [progress, setProgress] = useState<GenerationProgress | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<SelectedHunk | null>(null);
  const [busyQuestion, setBusyQuestion] = useState<string | null>(null);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const generated = state !== null;

  async function loadAll(id: string) {
    const p = await api.plans.get(id);
    setPlan(p);
    const [a, profile] = await Promise.all([api.assessments.get(p.assessmentId), api.profiles.get(p.profileId).catch(() => null)]);
    setAssessment(a);
    setAlias(profile?.alias ?? "");
    if (p.status !== "Draft" && p.status !== "Planned") {
      const [s, ms] = await Promise.all([api.plans.state(id), api.plans.measures(id)]);
      setState(s);
      setMeasures(Object.fromEntries(ms.map((m) => [m.id, m])));
    }
  }

  useEffect(() => {
    if (!planId) return;
    loadAll(planId).catch(() => setError("No se pudo cargar el plan."));
  }, [planId]);

  useEffect(() => () => { if (pollRef.current) clearInterval(pollRef.current); }, []);

  async function generate() {
    if (!planId) return;
    setGenerating(true);
    setError(null);
    setProgress(null);
    pollRef.current = setInterval(() => {
      api.plans.generateProgress(planId).then((p) => setProgress(p ?? null)).catch(() => {});
    }, 1000);
    try {
      const result = await api.plans.generate(planId);
      setState(result);
      const [ms, fresh] = await Promise.all([api.plans.measures(planId), api.plans.get(planId)]);
      setMeasures(Object.fromEntries(ms.map((m) => [m.id, m])));
      setPlan(fresh);
    } catch (e) {
      setError(e instanceof Error && e.message ? e.message : "No se pudo generar la adaptación. Comprueba la configuración de la IA (clave de Gemini).");
    } finally {
      if (pollRef.current) clearInterval(pollRef.current);
      pollRef.current = null;
      setGenerating(false);
      setProgress(null);
    }
  }

  const questionsById = useMemo(
    () => new Map<string, Question>((assessment?.sections ?? []).flatMap((s) => s.questions).map((q) => [q.id, q])),
    [assessment],
  );
  const validationByQuestion = useMemo(() => {
    const map = new Map<string, ValidationResult[]>();
    for (const v of state?.validationResults ?? []) {
      if (!v.questionId) continue;
      map.set(v.questionId, [...(map.get(v.questionId) ?? []), v]);
    }
    return map;
  }, [state]);
  const semaphoreByQuestion = useMemo(
    () => new Map<string, QuestionSemaphore>((state?.semaphore.questions ?? []).map((q) => [q.questionId, q])),
    [state],
  );

  async function run(adaptedQuestionId: string, action: () => Promise<PlanState>) {
    setBusyQuestion(adaptedQuestionId);
    setError(null);
    try {
      setState(await action());
      setSelected(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar el cambio.");
    } finally {
      setBusyQuestion(null);
    }
  }

  if (!plan || !assessment) return <div className="page wrap"><p className="muted">{error ?? "Cargando…"}</p></div>;

  const adapted = [...(state?.adaptedQuestions ?? [])].sort(
    (a, b) => (questionsById.get(a.questionId)?.order ?? 0) - (questionsById.get(b.questionId)?.order ?? 0));
  const planLevelIssues = (state?.validationResults ?? []).filter((v) => !v.questionId);
  const errorCount = (state?.validationResults ?? []).filter((v) => v.severity === "Error").length;
  const pendingProposals = adapted.filter((a) => a.proposal?.status === "Pending").length;

  const links = [
    "/upload",
    `/assessments/${assessment.id}/analysis`,
    `/assessments/${assessment.id}/adapt`,
    undefined,
    generated ? `/plans/${planId}/edit` : undefined,
    generated ? `/plans/${planId}/export` : undefined,
  ];

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>{generated ? "Comparador" : "Generación de textos"}</Eyebrow>
        <Stepper steps={PIPELINE_STEPS} current={3} links={links} />
        <h1 style={{ fontSize: 28, marginBottom: 4 }}>{assessment.title}</h1>
        <p className="muted" style={{ marginBottom: 16 }}>
          {alias && <strong>{alias} · </strong>}Nivel {plan.level} {plan.isCurricularChange && "· Adaptación curricular"}
          {plan.packId && <> · <Link to={`/packs/${plan.packId}`}>Volver al pack de aula</Link></>}
        </p>

        {plan.warnings.length > 0 && (
          <div className="card-panel" style={{ marginBottom: 24 }}>
            <h3 style={{ fontSize: 14, marginBottom: 8 }}>Avisos del plan</h3>
            <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13, color: "var(--text-secondary)" }}>
              {plan.warnings.map((w, i) => <li key={i}>{w}</li>)}
            </ul>
          </div>
        )}

        {!generated && (
          <div className="card-panel" style={{ textAlign: "center", padding: 40 }}>
            <p className="muted" style={{ marginBottom: 20 }}>
              El plan ya decidió qué medidas aplicar. Ahora la IA reescribe el texto de cada pregunta siguiendo exactamente esas medidas — nunca cambia
              puntos ni respuestas, y lo que pueda alterar lo evaluado solo lo propone.
            </p>
            <button className="btn" disabled={generating} onClick={generate}>
              {generating
                ? (progress && progress.total > 0 ? `Adaptando ${progress.current}/${progress.total}…` : "Generando…")
                : "Generar textos adaptados"}
            </button>
            {error && <p style={{ color: "var(--error)", marginTop: 16 }} role="alert">{error}</p>}
          </div>
        )}

        {generated && state && (
          <>
            <div className="card-panel" style={{ marginBottom: 20 }} data-testid="semaphore-summary">
              <div className="row spread">
                <div className="row">
                  <SemaphoreBadge level={state.semaphore.overall} />
                  <strong>
                    {errorCount === 0 ? "Sin errores de validación" : `${errorCount} error(es) de validación`}
                    {pendingProposals > 0 && ` · ${pendingProposals} propuesta(s) por decidir`}
                  </strong>
                </div>
                <div className="row">
                  <Link className="btn btn-sm btn-outline" to={`/plans/${planId}/history`}>Historial de cambios</Link>
                  <button className="btn btn-sm btn-outline" disabled={generating} onClick={generate}>Regenerar</button>
                </div>
              </div>
              {state.semaphore.overallReasons.length > 0 && (
                <ul style={{ margin: "10px 0 0", paddingLeft: 18, fontSize: 13, color: "var(--text-secondary)" }}>
                  {state.semaphore.overallReasons.map((r, i) => <li key={i}>{r}</li>)}
                </ul>
              )}
              <p className="muted" style={{ fontSize: 12, marginTop: 10 }}>
                <strong>Semáforo:</strong> verde = adaptación segura · naranja = revisa (pista, cambio de formato, reducción lingüística) ·
                rojo = posible cambio de criterio, respuesta, puntuación, contenido o nivel cognitivo.
              </p>
            </div>

            {state.documentOutdated && (
              <div style={{ marginBottom: 16 }}>
                <Callout kind="warning">
                  Ya habías editado el documento a mano, así que este cambio no se ha trasladado a él. Ábrelo en el editor para revisarlo
                  o descarta las ediciones allí para volver al documento generado.
                </Callout>
              </div>
            )}
            {error && <div style={{ marginBottom: 16 }}><Callout kind="error">{error}</Callout></div>}

            {planLevelIssues.length > 0 && (
              <div className="card-panel" style={{ marginBottom: 24 }}>
                {planLevelIssues.map((v) => (
                  <p key={v.id} style={{ fontSize: 13, marginBottom: 6 }}>
                    <SeverityBadge severity={v.severity} /> <span style={{ marginLeft: 8 }}>{v.message}</span>
                  </p>
                ))}
              </div>
            )}

            <div className="stack">
              {adapted.map((a) => (
                <QuestionCompare
                  key={a.id} a={a} plan={plan} question={questionsById.get(a.questionId)} measures={measures}
                  issues={validationByQuestion.get(a.questionId) ?? []} semaphore={semaphoreByQuestion.get(a.questionId)}
                  selected={selected} onSelect={setSelected} busy={busyQuestion === a.id}
                  onUndoHunk={(text, ruleId) => run(a.id, () => api.plans.updateAdapted(plan.id, a.id, { adaptedText: text, kind: "undo", ruleId }))}
                  onDecideProposal={(accept) => run(a.id, () => api.plans.decideProposal(plan.id, a.id, accept))}
                  onReview={(approved) => run(a.id, () => api.plans.review(plan.id, a.id, approved))}
                />
              ))}
            </div>

            <div className="row mt-32">
              <button className="btn" onClick={() => navigate(`/plans/${planId}/edit`)}>Continuar al editor visual</button>
              <button className="btn btn-outline" onClick={() => navigate(`/plans/${planId}/export`)}>Ir directo a exportar</button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function QuestionCompare({
  a, plan, question, measures, issues, semaphore, selected, onSelect, busy, onUndoHunk, onDecideProposal, onReview,
}: {
  a: AdaptedQuestion; plan: AdaptationPlan; question: Question | undefined; measures: Record<string, Measure>;
  issues: ValidationResult[]; semaphore: QuestionSemaphore | undefined; selected: SelectedHunk | null;
  onSelect: (h: SelectedHunk | null) => void; busy: boolean;
  onUndoHunk: (newText: string, ruleId: string) => void;
  onDecideProposal: (accept: boolean) => void; onReview: (approved: boolean) => void;
}) {
  const original = question?.originalText ?? "";
  const segments = useMemo(() => buildSegments(original, a.adaptedText), [original, a.adaptedText]);
  const hunks = hunksOf(segments);
  const activeHunk = selected?.adaptedQuestionId === a.id ? hunks.find((h) => h.id === selected.hunkId) ?? null : null;
  const entry = activeHunk ? explainHunk(activeHunk, a.changeLog) : null;
  const entryRule = entry ? measures[entry.ruleId] : undefined;
  const fallbackRules = (plan.resolvedRulesByQuestion[a.questionId] ?? [])
    .filter((r) => r.applied && !r.proposalOnly && measures[r.ruleId] && ["Text", "Support"].includes(measures[r.ruleId].kind))
    .map((r) => measures[r.ruleId]);
  const proposalPending = a.proposal?.status === "Pending";
  const proposalHunks = a.proposal ? hunksOf(buildSegments(a.adaptedText, a.proposal.proposedText)) : [];

  return (
    <div className="card-panel" data-question={(question?.order ?? 0) + 1} data-semaphore={semaphore?.level}>
      <div className="row spread" style={{ marginBottom: 8 }}>
        <div className="row">
          <strong>Pregunta {(question?.order ?? 0) + 1} · {a.points} pts</strong>
          {semaphore && <SemaphoreBadge level={semaphore.level} title={semaphore.reasons.join(" · ") || undefined} />}
          {a.teacherApproved && <span className="badge badge-success">Aprobada</span>}
        </div>
        <div className="row">
          {issues.map((v) => <span key={v.id} title={v.message}><SeverityBadge severity={v.severity} /></span>)}
        </div>
      </div>

      {semaphore && semaphore.level !== "Green" && (
        <ul style={{ margin: "0 0 10px", paddingLeft: 18, fontSize: 12, color: "var(--text-secondary)" }}>
          {semaphore.reasons.map((r, i) => <li key={i}>{r}</li>)}
        </ul>
      )}

      <div className="compare-cols">
        <div>
          <div className="comparator-col-label">Original</div>
          <HunkText segments={segments} side="original" selected={selected} onSelect={onSelect} adaptedQuestionId={a.id} />
        </div>
        <div style={{ borderColor: "var(--grad-from)" }}>
          <div className="comparator-col-label">Adaptado</div>
          <HunkText segments={segments} side="adapted" selected={selected} onSelect={onSelect} adaptedQuestionId={a.id} />
          {a.supports.length > 0 && (
            <ul className="support-list">
              {a.supports.map((s, i) => <li key={i}>{s}</li>)}
            </ul>
          )}
        </div>
      </div>

      {hunks.length > 0 && !activeHunk && (
        <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>{hunks.length} cambio(s) resaltado(s): púlsalos para ver la medida y el motivo, o deshacerlos uno a uno.</p>
      )}

      {activeHunk && (
        <div className="hunk-panel" data-testid="hunk-panel">
          <p><strong>Qué cambió:</strong> {activeHunk.removed.trim() ? <>«{activeHunk.removed.trim()}»</> : <em>(nada)</em>} → {activeHunk.added.trim() ? <>«{activeHunk.added.trim()}»</> : <em>(se quitó)</em>}</p>
          {entry ? (
            <>
              <p style={{ marginTop: 6 }}><strong>Medida que lo provocó:</strong> {entryRule?.description ?? entry.description}</p>
              <p style={{ marginTop: 6 }}><strong>Por qué:</strong> {entryRule?.rationale || entry.description}</p>
            </>
          ) : (
            <>
              <p style={{ marginTop: 6 }}><strong>Medidas de formato/estructura de esta pregunta:</strong></p>
              <ul style={{ margin: "4px 0 0", paddingLeft: 18 }}>
                {fallbackRules.map((r) => <li key={r.id}>{r.description} — <span className="muted">{r.rationale}</span></li>)}
                {fallbackRules.length === 0 && <li>Ajuste de formato del texto.</li>}
              </ul>
            </>
          )}
          <div className="row" style={{ marginTop: 10 }}>
            <button
              className="btn btn-sm btn-outline" disabled={busy}
              onClick={() => onUndoHunk(undoHunk(segments, activeHunk.id), entry?.ruleId ?? "")}
            >
              Deshacer este cambio
            </button>
            <button className="btn btn-sm btn-outline" onClick={() => onSelect(null)}>Cerrar</button>
          </div>
        </div>
      )}

      {a.proposal && (
        <div className="proposal-box" data-testid="proposal">
          <strong>
            {proposalPending ? "Propuesta de la IA (no aplicada)" : a.proposal.status === "Accepted" ? "Propuesta aceptada" : "Propuesta rechazada"}
          </strong>
          <p style={{ margin: "6px 0" }}>{a.proposal.reason}</p>
          {(a.proposal.ruleIds ?? []).length > 0 && (
            <p className="muted" style={{ fontSize: 12 }}>
              Medidas: {a.proposal.ruleIds.map((id) => measures[id]?.description ?? id).join("; ")}
            </p>
          )}
          <div className="compare-cols" style={{ marginTop: 8 }}>
            <div><div className="comparator-col-label">Texto actual</div>{a.adaptedText}</div>
            <div className="proposal"><div className="comparator-col-label">Texto propuesto</div>{a.proposal.proposedText}</div>
          </div>
          {proposalPending && (
            <>
              <p className="muted" style={{ fontSize: 12, margin: "8px 0 0" }}>
                Esta medida podría cambiar lo que realmente se evalúa ({proposalHunks.length} cambio(s) respecto al texto actual), por eso no se aplica sola.
              </p>
              <div className="row" style={{ marginTop: 10 }}>
                <button className="btn btn-sm" disabled={busy} onClick={() => onDecideProposal(true)}>Aplicar la propuesta</button>
                <button className="btn btn-sm btn-outline" disabled={busy} onClick={() => onDecideProposal(false)}>Descartarla</button>
              </div>
            </>
          )}
        </div>
      )}

      <div className="change-actions">
        <button className="btn-sm btn btn-outline" disabled={busy} onClick={() => onReview(true)}>
          {a.teacherApproved ? "✓ Aprobado" : "Aceptar pregunta"}
        </button>
        <button className="btn-sm btn btn-outline" disabled={busy} onClick={() => onReview(false)}>Rechazar (usar original)</button>
      </div>
    </div>
  );
}
