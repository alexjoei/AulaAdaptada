import type { ReactNode } from "react";
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

export function Stepper({ steps, current }: { steps: string[]; current: number }) {
  return (
    <div className="stepper">
      {steps.map((label, i) => (
        <span key={label} className={`step${i === current ? " current" : ""}${i < current ? " done" : ""}`}>
          <span className="step-num">{i < current ? "✓" : i + 1}</span>
          {label}
        </span>
      ))}
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
