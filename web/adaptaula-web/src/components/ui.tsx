import type { ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import type { ValidationSeverity } from "../api/types";

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

export const PIPELINE_STEPS = ["Subida", "Análisis", "Adaptación", "Comparador", "Generación"];

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
