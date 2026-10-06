import { useState } from "react";
import { api } from "../api/client";
import type { TextTool, TextToolResponse } from "../api/types";
import { Callout, Modal } from "./ui";

export const TEXT_TOOLS: { tool: TextTool; label: string; hint: string }[] = [
  { tool: "Shorten", label: "Acortar", hint: "Quita lo redundante." },
  { tool: "SimplifySyntax", label: "Simplificar sintaxis", hint: "Frases más cortas y directas." },
  { tool: "SplitIntoSteps", label: "Dividir en pasos", hint: "Una acción por paso numerado." },
  { tool: "HighlightKeywords", label: "Destacar palabras clave", hint: "Negrita en palabras y verbos clave." },
  { tool: "AddExample", label: "Añadir ejemplo", hint: "Ejemplo de formato, nunca la solución." },
  { tool: "WordBank", label: "Banco de palabras", hint: "Palabras útiles para responder." },
  { tool: "Hint", label: "Pista", hint: "Orienta sin dar la respuesta." },
  { tool: "Checklist", label: "Checklist", hint: "Lista para revisar la respuesta." },
  { tool: "Organizer", label: "Organizador", hint: "Esquema o tabla de huecos." },
  { tool: "ReadAloud", label: "Lectura en voz alta", hint: "Escucha el texto tal cual." },
];

interface Props {
  text: string;
  questionId: string | null;
  questionContext?: string;
  onApply: (proposal: string, tool: TextTool, original: string) => void;
  onClose: () => void;
}

/** "Original → Propuesta" before anything is applied (V2 §10). The expected answer is checked on the server and never reaches the AI. */
export default function TextToolDialog({ text, questionId, questionContext, onApply, onClose }: Props) {
  const [tool, setTool] = useState<TextTool | null>(null);
  const [result, setResult] = useState<TextToolResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(next: TextTool) {
    setTool(next);
    setResult(null);
    setError(null);
    setBusy(true);
    try {
      setResult(await api.ai.textTool({ tool: next, text, questionId: questionId ?? undefined, questionContext }));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo ejecutar la herramienta.");
    } finally {
      setBusy(false);
    }
  }

  function speak() {
    if (!("speechSynthesis" in window)) { setError("Tu navegador no puede leer en voz alta."); return; }
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text.replace(/\*\*/g, ""));
    utterance.lang = "es-ES";
    window.speechSynthesis.speak(utterance);
  }

  return (
    <Modal title="Herramientas de IA sobre el texto" onClose={onClose}>
      <p className="muted" style={{ fontSize: 13, marginBottom: 12 }}>
        Texto seleccionado: <em>«{text.length > 160 ? text.slice(0, 160) + "…" : text}»</em>
      </p>

      <div className="row" style={{ marginBottom: 16 }} role="group" aria-label="Herramientas">
        {TEXT_TOOLS.map((t) => (
          <button
            key={t.tool} type="button" title={t.hint} disabled={busy}
            className={`tag-chip${tool === t.tool ? " selected" : ""}`} onClick={() => run(t.tool)} data-tool={t.tool}
          >
            {t.label}
          </button>
        ))}
      </div>

      {busy && <p className="muted">Preparando la propuesta…</p>}
      {error && <Callout kind="error">{error}</Callout>}

      {result && tool && (
        <div data-testid="tool-result">
          <div className="compare-cols">
            <div><div className="comparator-col-label">Original</div>{result.original}</div>
            <div className="proposal"><div className="comparator-col-label">Propuesta</div>{result.proposal}</div>
          </div>
          <p className="muted" style={{ fontSize: 13, margin: "10px 0" }}>{result.note}</p>
          {result.warnings.map((w, i) => <div key={i} style={{ marginBottom: 6 }}><Callout kind="warning">{w}</Callout></div>)}
          <div className="row" style={{ marginTop: 12 }}>
            {tool === "ReadAloud"
              ? <button className="btn btn-sm" onClick={speak}>▶ Escuchar</button>
              : <button className="btn btn-sm" onClick={() => onApply(result.proposal, tool, result.original)}>Aplicar al documento</button>}
            <button className="btn btn-sm btn-outline" onClick={onClose}>Descartar</button>
          </div>
        </div>
      )}
    </Modal>
  );
}
