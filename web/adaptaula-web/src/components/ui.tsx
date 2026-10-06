import { useEffect, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import type { MeasureClassification, SemaphoreLevel, ValidationSeverity } from "../api/types";

export function Eyebrow({ children }: { children: ReactNode }) {
  return (
    <span className="eyebrow">
      <span className="pulse-dot" aria-hidden="true" />
      {children}
    </span>
  );
}

const severityClass: Record<ValidationSeverity, string> = {
  Error: "badge badge-error",
  Warning: "badge badge-warning",
  Review: "badge badge-review",
};

export function SeverityBadge({ severity }: { severity: ValidationSeverity }) {
  const label = severity === "Error" ? "Error" : severity === "Warning" ? "Aviso" : "Revisión";
  return <span className={severityClass[severity]}>{label}</span>;
}

const SEMAPHORE: Record<SemaphoreLevel, { cls: string; label: string; hint: string }> = {
  Green: { cls: "sem sem-green", label: "Verde", hint: "Adaptación segura" },
  Orange: { cls: "sem sem-orange", label: "Naranja", hint: "Conviene revisarla (pista, cambio de formato, reducción lingüística)" },
  Red: { cls: "sem sem-red", label: "Rojo", hint: "Posible cambio de criterio, respuesta, puntuación, contenido o nivel cognitivo" },
};

/** Pedagogical traffic light (V2 §16). */
export function SemaphoreBadge({ level, title }: { level: SemaphoreLevel; title?: string }) {
  const s = SEMAPHORE[level];
  return <span className={s.cls} title={title ?? s.hint} data-semaphore={level}>{s.label}</span>;
}

const CLASSIFICATION: Record<MeasureClassification, { cls: string; label: string }> = {
  Recommended: { cls: "class-badge class-recommended", label: "Recomendada" },
  Optional: { cls: "class-badge class-optional", label: "Opcional" },
  RequiresTeacherDecision: { cls: "class-badge class-decision", label: "Requiere decisión docente" },
  NotRecommended: { cls: "class-badge class-not_recommended", label: "No recomendada" },
};

export function ClassificationBadge({ value }: { value: MeasureClassification }) {
  const c = CLASSIFICATION[value];
  return <span className={c.cls}>{c.label}</span>;
}

export const PIPELINE_STEPS = ["Subida", "Análisis", "Adaptación", "Comparador", "Editor", "Exportar"];

/** `links[i]` is the path to navigate to when step `i` is clicked; omit/undefined to leave it inert. */
export function Stepper({ steps, current, links }: { steps: string[]; current: number; links?: (string | undefined)[] }) {
  const navigate = useNavigate();
  return (
    <div className="stepper">
      {steps.map((label, i) => {
        const href = i !== current ? links?.[i] : undefined;
        return (
          <span
            key={label}
            className={`step${i === current ? " current" : ""}${i < current ? " done" : ""}${href ? " clickable" : ""}`}
            onClick={href ? () => navigate(href) : undefined}
            onKeyDown={href ? (e) => { if (e.key === "Enter" || e.key === " ") navigate(href); } : undefined}
            role={href ? "button" : undefined}
            tabIndex={href ? 0 : undefined}
          >
            <span className="step-num">{i < current ? "✓" : i + 1}</span>
            {label}
          </span>
        );
      })}
    </div>
  );
}

export function EmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="card-panel" style={{ textAlign: "center", padding: "48px 28px" }}>
      <h3 style={{ marginBottom: 8 }}>{title}</h3>
      <p className="muted">{description}</p>
    </div>
  );
}

export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={title}>
        <div className="row spread" style={{ marginBottom: 8 }}>
          <h3>{title}</h3>
          <button className="btn-sm btn-outline btn" onClick={onClose} aria-label="Cerrar">Cerrar</button>
        </div>
        {children}
      </div>
    </div>
  );
}

export function Callout({ kind = "info", children }: { kind?: "info" | "warning" | "error" | "success"; children: ReactNode }) {
  return <div className={`callout callout-${kind}`} role={kind === "error" ? "alert" : undefined}>{children}</div>;
}

/** Plain-language labels for the ValidationCode enum, shown instead of the raw code. */
export const VALIDATION_LABELS: Record<string, string> = {
  PointsChanged: "Puntuación",
  AnswerChanged: "Respuesta",
  ContentDropped: "Contenido perdido",
  ReadingConstructBypassed: "Lectura evitada",
  SpellingConstructBypassed: "Ortografía evitada",
  HintRevealsAnswer: "Pista que regala la respuesta",
  DifficultyReduced: "Exigencia reducida",
  CurricularChange: "Cambio curricular",
  LowExtractionConfidence: "Extracción poco fiable",
  UnsupportedConflict: "Conflicto entre medidas",
  ProtectedVocabularyMissing: "Vocabulario protegido",
  QuestionCountChanged: "Número de preguntas",
  LanguageChanged: "Idioma",
  NumberingChanged: "Numeración",
  AnswerSpaceMissing: "Espacio de respuesta",
  ExcessiveHints: "Demasiadas pistas",
  CriteriaCoverage: "Cobertura de criterios",
  CognitiveDemandChanged: "Demanda cognitiva",
  ImageMayRevealAnswer: "Imagen y respuesta",
  ProposalPending: "Propuesta pendiente",
  QuestionMissing: "Pregunta ausente",
};
