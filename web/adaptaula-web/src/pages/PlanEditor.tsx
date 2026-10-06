import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { EditorContent, useEditor, type Editor } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import { TextStyleKit } from "@tiptap/extension-text-style";
import Highlight from "@tiptap/extension-highlight";
import TextAlign from "@tiptap/extension-text-align";
import { Table, TableCell, TableHeader, TableRow } from "@tiptap/extension-table";
import { api } from "../api/client";
import type { DocumentStyle, PlanState, TextTool } from "../api/types";
import { Callout, Eyebrow, SemaphoreBadge, SeverityBadge, Stepper, PIPELINE_STEPS, VALIDATION_LABELS } from "../components/ui";
import TextToolDialog from "../components/TextToolDialog";
import { AnswerSpace, BoxBlock, PageBreak, QuestionBlock, RoleImage, Spacing, StimulusBlock } from "../editor/extensions";

const FONTS = ["Arial", "Verdana", "Tahoma", "Calibri", "Trebuchet MS", "Georgia", "Times New Roman", "Comic Sans MS"];
const SIZES = [10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32];
const IMAGE_ROLES = [
  { value: "content", label: "Contenido de la pregunta" },
  { value: "decorative", label: "Decorativa" },
  { value: "access", label: "Ayuda de acceso" },
  { value: "clue", label: "Pista" },
  { value: "reveals_answer", label: "Revela la respuesta" },
];

const markup = (s: string) =>
  s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>").replace(/\n/g, "<br>");

/** Reads an image file and shrinks it so a document with several images stays light. */
function fileToDataUri(file: File, maxSide = 1200): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error("No se pudo leer la imagen."));
    reader.onload = () => {
      const original = String(reader.result);
      if (file.type === "image/gif" || file.type === "image/svg+xml") { resolve(original); return; }
      const img = new window.Image();
      img.onerror = () => reject(new Error("La imagen no es válida."));
      img.onload = () => {
        const scale = Math.min(1, maxSide / Math.max(img.width, img.height));
        const canvas = document.createElement("canvas");
        canvas.width = Math.round(img.width * scale);
        canvas.height = Math.round(img.height * scale);
        canvas.getContext("2d")!.drawImage(img, 0, 0, canvas.width, canvas.height);
        resolve(canvas.toDataURL(file.type === "image/png" ? "image/png" : "image/jpeg", 0.88));
      };
      img.src = original;
    };
    reader.readAsDataURL(file);
  });
}

function questionIdAtSelection(editor: Editor): string | null {
  const { $from } = editor.state.selection;
  for (let depth = $from.depth; depth > 0; depth--) {
    const node = $from.node(depth);
    if (node.type.name === "questionBlock") return (node.attrs.questionId as string) ?? null;
  }
  return null;
}

function Btn({ editor, label, title, active, onClick, disabled }: {
  editor: Editor | null; label: string; title: string; active?: boolean; onClick: () => void; disabled?: boolean;
}) {
  return (
    <button
      type="button" className={`tb-btn${active ? " active" : ""}`} title={title} aria-label={title} aria-pressed={active}
      disabled={disabled || !editor} onMouseDown={(e) => e.preventDefault()} onClick={onClick}
    >
      {label}
    </button>
  );
}

export default function PlanEditor() {
  const { id: planId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [loaded, setLoaded] = useState<{ html: string; style: DocumentStyle; title: string; assessmentId: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!planId) return;
    Promise.all([api.plans.document(planId), api.plans.get(planId)])
      .then(([doc, plan]) => setLoaded({ html: doc.html, style: doc.style, title: doc.title, assessmentId: plan.assessmentId }))
      .catch((e) => setError(e instanceof Error ? e.message : "No se pudo abrir el documento."));
  }, [planId]);

  if (error) return <div className="page wrap"><Callout kind="error">{error}</Callout><p style={{ marginTop: 12 }}><Link to={`/plans/${planId}`}>Volver al comparador</Link></p></div>;
  if (!loaded || !planId) return <div className="page wrap"><p className="muted">Abriendo el editor…</p></div>;

  return <EditorScreen key={planId} planId={planId} initial={loaded} navigate={navigate} />;
}

function EditorScreen({ planId, initial, navigate }: {
  planId: string; initial: { html: string; style: DocumentStyle; title: string; assessmentId: string }; navigate: ReturnType<typeof useNavigate>;
}) {
  const [style, setStyle] = useState<DocumentStyle>(initial.style);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [state, setState] = useState<PlanState | null>(null);
  const [message, setMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const [tool, setTool] = useState<{ text: string; from: number; to: number; questionId: string | null; context: string } | null>(null);
  const [bubble, setBubble] = useState<{ x: number; y: number } | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const [answerLines, setAnswerLines] = useState(4);
  const [answerKind, setAnswerKind] = useState<"blank" | "ruled" | "grid">("ruled");

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ heading: { levels: [1, 2, 3] }, link: false }),
      TextStyleKit,
      Spacing,
      Highlight.configure({ multicolor: true }),
      TextAlign.configure({ types: ["heading", "paragraph"] }),
      RoleImage.configure({ allowBase64: true }),
      Table.configure({ resizable: false }),
      TableRow, TableHeader, TableCell,
      QuestionBlock, StimulusBlock, BoxBlock, AnswerSpace, PageBreak,
    ],
    content: initial.html,
    shouldRerenderOnTransaction: true,
    onUpdate: () => setDirty(true),
    onSelectionUpdate: ({ editor: e }) => updateBubble(e),
  });

  function updateBubble(e: Editor) {
    const { from, to, empty } = e.state.selection;
    if (empty || from === to || e.isActive("image")) { setBubble(null); return; }
    try {
      const start = e.view.coordsAtPos(from);
      setBubble({ x: Math.max(8, start.left), y: Math.max(8, start.top - 46) });
    } catch {
      setBubble(null);
    }
  }

  useEffect(() => {
    const warn = (ev: BeforeUnloadEvent) => { if (dirty) { ev.preventDefault(); ev.returnValue = ""; } };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const save = useCallback(async (): Promise<PlanState | null> => {
    if (!editor) return null;
    setSaving(true);
    setMessage(null);
    try {
      const result = await api.plans.saveDocument(planId, editor.getHTML(), style);
      setState(result);
      setDirty(false);
      const errors = result.validationResults.filter((v) => v.severity === "Error").length;
      setMessage(errors === 0
        ? { kind: "success", text: "Guardado y validado: no hay errores que impidan exportar." }
        : { kind: "error", text: `Guardado, pero hay ${errors} error(es) de validación que impiden exportar. Revísalos abajo.` });
      return result;
    } catch (e) {
      setMessage({ kind: "error", text: e instanceof Error ? e.message : "No se pudo guardar el documento." });
      return null;
    } finally {
      setSaving(false);
    }
  }, [editor, planId, style]);

  async function resetDocument() {
    if (!editor) return;
    if (!window.confirm("Se descartarán todas tus ediciones manuales y volverá el documento generado. ¿Continuar?")) return;
    setSaving(true);
    try {
      const result = await api.plans.resetDocument(planId);
      setState(result);
      const doc = await api.plans.document(planId);
      editor.commands.setContent(doc.html);
      setStyle(doc.style);
      setDirty(false);
      setMessage({ kind: "success", text: "Documento restablecido al generado." });
    } catch (e) {
      setMessage({ kind: "error", text: e instanceof Error ? e.message : "No se pudo restablecer." });
    } finally {
      setSaving(false);
    }
  }

  async function goExport() {
    if (dirty) { const saved = await save(); if (!saved) return; }
    navigate(`/plans/${planId}/export`);
  }

  function patchStyle(change: Partial<DocumentStyle>) {
    setStyle((prev) => ({ ...prev, ...change }));
    setDirty(true);
  }

  async function onImageFile(file: File | undefined) {
    if (!file || !editor) return;
    if (!file.type.startsWith("image/")) { setMessage({ kind: "error", text: "Elige un archivo de imagen." }); return; }
    try {
      const src = await fileToDataUri(file);
      editor.chain().focus().setImage({ src, alt: file.name.replace(/\.[^.]+$/, "") }).run();
    } catch (e) {
      setMessage({ kind: "error", text: e instanceof Error ? e.message : "No se pudo insertar la imagen." });
    }
  }

  function openTool() {
    if (!editor) return;
    const { from, to } = editor.state.selection;
    const text = editor.state.doc.textBetween(from, to, "\n");
    if (!text.trim()) return;
    const questionId = questionIdAtSelection(editor);
    setBubble(null);
    setTool({ text, from, to, questionId, context: "" });
  }

  async function applyTool(proposal: string, toolName: TextTool, original: string) {
    if (!editor || !tool) return;
    editor.chain().focus().insertContentAt({ from: tool.from, to: tool.to }, markup(proposal)).run();
    api.plans.logTextTool(planId, { questionId: tool.questionId, tool: toolName, before: original, after: proposal }).catch(() => {});
    setTool(null);
    setMessage({ kind: "success", text: "Propuesta aplicada. Recuerda guardar para validarla." });
  }

  const paper: CSSProperties = {
    fontFamily: `${style.fontFamily}, Arial, sans-serif`,
    fontSize: `${style.fontSizePt}pt`,
    lineHeight: style.lineSpacing,
    letterSpacing: `${style.letterSpacingPt}pt`,
    wordSpacing: `${style.wordSpacingPt}pt`,
    textAlign: style.align === "justify" ? "justify" : "left",
    padding: `${style.marginMm}mm`,
    maxWidth: "210mm",
    color: style.highContrast ? "#000" : undefined,
  };

  const a = (name: string, attrs?: Record<string, unknown>) => !!editor?.isActive(name, attrs);
  const imageSelected = a("image");
  const imageRole = (editor?.getAttributes("image").role as string | undefined) ?? "content";
  const inTable = a("table");

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Editor visual</Eyebrow>
        <Stepper
          steps={PIPELINE_STEPS} current={4}
          links={["/upload", `/assessments/${initial.assessmentId}/analysis`, `/assessments/${initial.assessmentId}/adapt`, `/plans/${planId}`, undefined, `/plans/${planId}/export`]}
        />
        <div className="row spread" style={{ marginBottom: 14 }}>
          <div>
            <h1 style={{ fontSize: 26, marginBottom: 2 }}>{initial.title}</h1>
            <p className="muted" style={{ fontSize: 13 }}>
              Edita directamente el documento que se exportará. {dirty ? <strong style={{ color: "var(--warning)" }}>Cambios sin guardar</strong> : "Todo guardado."}
            </p>
          </div>
          <div className="row">
            <button className="btn btn-outline btn-sm" disabled={saving} onClick={resetDocument}>Descartar ediciones</button>
            <button className="btn btn-sm" disabled={saving || !dirty && state !== null} onClick={save}>{saving ? "Guardando…" : "Guardar y validar"}</button>
            <button className="btn btn-sm btn-outline" disabled={saving} onClick={goExport}>Exportar →</button>
          </div>
        </div>

        {message && <div style={{ marginBottom: 12 }}><Callout kind={message.kind}>{message.text}</Callout></div>}

        <div className="editor-shell">
          <div>
            <div className="editor-toolbar" role="toolbar" aria-label="Formato del documento" data-testid="toolbar">
              <div className="tb-group">
                <Btn editor={editor} label="↶" title="Deshacer" onClick={() => editor?.chain().focus().undo().run()} disabled={!editor?.can().undo()} />
                <Btn editor={editor} label="↷" title="Rehacer" onClick={() => editor?.chain().focus().redo().run()} disabled={!editor?.can().redo()} />
              </div>
              <div className="tb-group">
                <select className="tb-select" aria-label="Fuente" defaultValue="" onChange={(e) => { const v = e.target.value; if (v) editor?.chain().focus().setFontFamily(v).run(); else editor?.chain().focus().unsetFontFamily().run(); }}>
                  <option value="">Fuente del alumno</option>
                  {FONTS.map((f) => <option key={f} value={f}>{f}</option>)}
                </select>
                <select className="tb-select" aria-label="Tamaño" defaultValue="" onChange={(e) => { const v = e.target.value; if (v) editor?.chain().focus().setFontSize(`${v}pt`).run(); else editor?.chain().focus().unsetFontSize().run(); }}>
                  <option value="">Tamaño</option>
                  {SIZES.map((s) => <option key={s} value={s}>{s} pt</option>)}
                </select>
                <select
                  className="tb-select" aria-label="Estilo de párrafo" value={a("heading", { level: 1 }) ? "1" : a("heading", { level: 2 }) ? "2" : a("heading", { level: 3 }) ? "3" : "0"}
                  onChange={(e) => { const v = Number(e.target.value); if (v === 0) editor?.chain().focus().setParagraph().run(); else editor?.chain().focus().setHeading({ level: v as 1 | 2 | 3 }).run(); }}
                >
                  <option value="0">Párrafo</option><option value="1">Título 1</option><option value="2">Título 2</option><option value="3">Título 3</option>
                </select>
              </div>
              <div className="tb-group">
                <Btn editor={editor} label="N" title="Negrita" active={a("bold")} onClick={() => editor?.chain().focus().toggleBold().run()} />
                <Btn editor={editor} label="K" title="Cursiva" active={a("italic")} onClick={() => editor?.chain().focus().toggleItalic().run()} />
                <Btn editor={editor} label="S" title="Subrayado" active={a("underline")} onClick={() => editor?.chain().focus().toggleUnderline().run()} />
                <Btn editor={editor} label="T̶" title="Tachado" active={a("strike")} onClick={() => editor?.chain().focus().toggleStrike().run()} />
              </div>
              <div className="tb-group">
                <label className="muted" style={{ fontSize: 11 }}>Color
                  <input className="tb-color" type="color" aria-label="Color del texto" defaultValue="#000000" onChange={(e) => editor?.chain().focus().setColor(e.target.value).run()} />
                </label>
                <label className="muted" style={{ fontSize: 11 }}>Resaltado
                  <input className="tb-color" type="color" aria-label="Color de resaltado" defaultValue="#fff59d" onChange={(e) => editor?.chain().focus().setHighlight({ color: e.target.value }).run()} />
                </label>
                <Btn editor={editor} label="✕" title="Quitar resaltado" onClick={() => editor?.chain().focus().unsetHighlight().run()} />
              </div>
              <div className="tb-group">
                <Btn editor={editor} label="⇤" title="Alinear a la izquierda" active={a("paragraph", { textAlign: "left" })} onClick={() => editor?.chain().focus().setTextAlign("left").run()} />
                <Btn editor={editor} label="≡" title="Centrar" active={a("paragraph", { textAlign: "center" })} onClick={() => editor?.chain().focus().setTextAlign("center").run()} />
                <Btn editor={editor} label="⇥" title="Alinear a la derecha" active={a("paragraph", { textAlign: "right" })} onClick={() => editor?.chain().focus().setTextAlign("right").run()} />
                <Btn editor={editor} label="☰" title="Justificar" active={a("paragraph", { textAlign: "justify" })} onClick={() => editor?.chain().focus().setTextAlign("justify").run()} />
              </div>
              <div className="tb-group">
                <select className="tb-select" aria-label="Interlineado de la selección" defaultValue="" onChange={(e) => { const v = e.target.value; if (v) editor?.chain().focus().setLineHeight(v).run(); else editor?.chain().focus().unsetLineHeight().run(); }}>
                  <option value="">Interlineado</option><option value="1">1</option><option value="1.15">1,15</option><option value="1.5">1,5</option><option value="2">2</option>
                </select>
                <select className="tb-select" aria-label="Espaciado entre letras" defaultValue="" onChange={(e) => editor?.chain().focus().setLetterSpacing(e.target.value || "0").run()}>
                  <option value="">Letras</option><option value="0.5pt">+0,5</option><option value="1pt">+1</option><option value="2pt">+2</option><option value="0">Normal</option>
                </select>
                <select className="tb-select" aria-label="Espaciado entre palabras" defaultValue="" onChange={(e) => editor?.chain().focus().setWordSpacing(e.target.value || "0").run()}>
                  <option value="">Palabras</option><option value="2pt">+2</option><option value="4pt">+4</option><option value="8pt">+8</option><option value="0">Normal</option>
                </select>
              </div>
              <div className="tb-group">
                <Btn editor={editor} label="•" title="Lista con viñetas" active={a("bulletList")} onClick={() => editor?.chain().focus().toggleBulletList().run()} />
                <Btn editor={editor} label="1." title="Lista numerada" active={a("orderedList")} onClick={() => editor?.chain().focus().toggleOrderedList().run()} />
                <Btn editor={editor} label="▦" title="Insertar tabla" onClick={() => editor?.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()} />
                {inTable && <Btn editor={editor} label="🗑▦" title="Eliminar tabla" onClick={() => editor?.chain().focus().deleteTable().run()} />}
              </div>
              <div className="tb-group">
                <Btn editor={editor} label="🖼" title="Insertar imagen" onClick={() => fileInput.current?.click()} />
                <Btn editor={editor} label="⤓" title="Salto de página" onClick={() => editor?.chain().focus().insertPageBreak().run()} />
                <Btn editor={editor} label="▭" title="Envolver en un bloque" active={a("boxBlock")} onClick={() => (a("boxBlock") ? editor?.chain().focus().lift("boxBlock").run() : editor?.chain().focus().wrapIn("boxBlock").run())} />
                <Btn editor={editor} label="✨ IA" title="Herramientas de IA sobre el texto seleccionado" onClick={openTool} />
              </div>
              <input ref={fileInput} type="file" accept="image/*" hidden onChange={(e) => { onImageFile(e.target.files?.[0]); e.target.value = ""; }} />
            </div>

            <div className="editor-paper" style={paper} data-testid="editor-paper" data-contrast={style.highContrast}>
              <EditorContent editor={editor} />
            </div>
          </div>

          <aside className="editor-side" aria-label="Opciones del documento">
            <div className="card-panel">
              <h3 style={{ fontSize: 15, marginBottom: 10 }}>Página y texto base</h3>
              <div className="field" style={{ marginBottom: 10 }}>
                <label htmlFor="st-font">Fuente</label>
                <select id="st-font" value={style.fontFamily} onChange={(e) => patchStyle({ fontFamily: e.target.value })}>
                  {[...new Set([style.fontFamily, ...FONTS])].map((f) => <option key={f}>{f}</option>)}
                </select>
              </div>
              <div className="row" style={{ marginBottom: 10 }}>
                <div className="field" style={{ marginBottom: 0, width: 110 }}>
                  <label htmlFor="st-size">Tamaño (pt)</label>
                  <input id="st-size" type="number" min={8} max={40} step={0.5} value={style.fontSizePt} onChange={(e) => patchStyle({ fontSizePt: Number(e.target.value) || 11 })} />
                </div>
                <div className="field" style={{ marginBottom: 0, width: 110 }}>
                  <label htmlFor="st-line">Interlineado</label>
                  <input id="st-line" type="number" min={1} max={3} step={0.05} value={style.lineSpacing} onChange={(e) => patchStyle({ lineSpacing: Number(e.target.value) || 1.2 })} />
                </div>
              </div>
              <div className="row" style={{ marginBottom: 10 }}>
                <div className="field" style={{ marginBottom: 0, width: 110 }}>
                  <label htmlFor="st-letter">Entre letras (pt)</label>
                  <input id="st-letter" type="number" min={0} max={6} step={0.1} value={style.letterSpacingPt} onChange={(e) => patchStyle({ letterSpacingPt: Number(e.target.value) || 0 })} />
                </div>
                <div className="field" style={{ marginBottom: 0, width: 110 }}>
                  <label htmlFor="st-word">Entre palabras (pt)</label>
                  <input id="st-word" type="number" min={0} max={20} step={0.5} value={style.wordSpacingPt} onChange={(e) => patchStyle({ wordSpacingPt: Number(e.target.value) || 0 })} />
                </div>
              </div>
              <div className="row" style={{ marginBottom: 10 }}>
                <div className="field" style={{ marginBottom: 0, width: 110 }}>
                  <label htmlFor="st-margin">Márgenes (mm)</label>
                  <input id="st-margin" type="number" min={5} max={60} value={style.marginMm} onChange={(e) => patchStyle({ marginMm: Number(e.target.value) || 18 })} />
                </div>
                <div className="field" style={{ marginBottom: 0, width: 110 }}>
                  <label htmlFor="st-align">Alineación</label>
                  <select id="st-align" value={style.align} onChange={(e) => patchStyle({ align: e.target.value })}>
                    <option value="left">Izquierda</option><option value="justify">Justificada</option>
                  </select>
                </div>
              </div>
              <label className="checkbox-row" style={{ marginBottom: 6 }}>
                <input type="checkbox" checked={style.highContrast} onChange={(e) => patchStyle({ highContrast: e.target.checked })} />
                Alto contraste (texto negro)
              </label>
              <label className="checkbox-row">
                <input type="checkbox" checked={style.pageBreakPerQuestion} onChange={(e) => patchStyle({ pageBreakPerQuestion: e.target.checked })} />
                Una pregunta por página
              </label>
              <p className="field-hint" style={{ marginTop: 8 }}>
                Esto se aplica al texto que no tenga un formato propio. Lo que formatees con la barra tiene prioridad. «Una pregunta por página» se aplica al regenerar.
              </p>
            </div>

            <div className="card-panel">
              <h3 style={{ fontSize: 15, marginBottom: 10 }}>Espacio para responder</h3>
              <div className="row" style={{ marginBottom: 10 }}>
                <div className="field" style={{ marginBottom: 0, width: 90 }}>
                  <label htmlFor="as-lines">Líneas</label>
                  <input id="as-lines" type="number" min={1} max={30} value={answerLines} onChange={(e) => setAnswerLines(Math.max(1, Math.min(30, Number(e.target.value) || 1)))} />
                </div>
                <div className="field" style={{ marginBottom: 0 }}>
                  <label htmlFor="as-kind">Tipo</label>
                  <select id="as-kind" value={answerKind} onChange={(e) => setAnswerKind(e.target.value as typeof answerKind)}>
                    <option value="blank">En blanco</option><option value="ruled">Con líneas</option><option value="grid">Cuadrícula</option>
                  </select>
                </div>
              </div>
              <button type="button" className="btn btn-sm btn-outline" onClick={() => editor?.chain().focus().insertAnswerSpace({ lines: answerLines, ruled: answerKind === "ruled", grid: answerKind === "grid" }).run()}>
                Insertar espacio
              </button>
            </div>

            {imageSelected && (
              <div className="card-panel" data-testid="image-panel">
                <h3 style={{ fontSize: 15, marginBottom: 10 }}>Imagen seleccionada</h3>
                <div className="field" style={{ marginBottom: 10 }}>
                  <label htmlFor="img-role">¿Para qué sirve esta imagen?</label>
                  <select id="img-role" value={imageRole} onChange={(e) => editor?.chain().focus().setImageRole(e.target.value).run()}>
                    {IMAGE_ROLES.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
                  </select>
                </div>
                <div className="field" style={{ marginBottom: 10 }}>
                  <label htmlFor="img-alt">Texto alternativo</label>
                  <input id="img-alt" value={(editor?.getAttributes("image").alt as string) ?? ""} onChange={(e) => editor?.chain().focus().updateAttributes("image", { alt: e.target.value }).run()} />
                </div>
                <div className="field" style={{ marginBottom: 10 }}>
                  <label htmlFor="img-width">Ancho (px)</label>
                  <input id="img-width" type="number" min={40} max={720} value={Number(editor?.getAttributes("image").width) || ""} onChange={(e) => editor?.chain().focus().updateAttributes("image", { width: e.target.value || null }).run()} />
                </div>
                {(imageRole === "clue" || imageRole === "reveals_answer") && (
                  <Callout kind="warning">Esta imagen puede facilitar la respuesta correcta: al guardar se marcará para que la revises antes de exportar.</Callout>
                )}
                <button type="button" className="btn btn-sm btn-outline" style={{ marginTop: 10 }} onClick={() => editor?.chain().focus().deleteSelection().run()}>Eliminar imagen</button>
              </div>
            )}

            <div className="card-panel" data-testid="validation-panel">
              <div className="row spread" style={{ marginBottom: 8 }}>
                <h3 style={{ fontSize: 15 }}>Validación</h3>
                {state && <SemaphoreBadge level={state.semaphore.overall} />}
              </div>
              {!state && <p className="muted" style={{ fontSize: 13 }}>Pulsa «Guardar y validar» para comprobar contenido, puntuación, vocabulario protegido, idioma, numeración, espacios de respuesta y pistas.</p>}
              {state && state.validationResults.length === 0 && <p style={{ fontSize: 13 }}>✓ Sin avisos.</p>}
              {state?.validationResults.map((v) => (
                <p key={v.id} style={{ fontSize: 12, margin: "0 0 8px" }}>
                  <SeverityBadge severity={v.severity} /> <strong>{VALIDATION_LABELS[v.code] ?? v.code}</strong> — {v.message}
                </p>
              ))}
            </div>
          </aside>
        </div>
      </div>

      {bubble && !tool && (
        <div className="sel-bubble" style={{ left: bubble.x, top: bubble.y }} role="toolbar" aria-label="Acciones sobre la selección">
          <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={openTool}>✨ Herramientas de IA</button>
        </div>
      )}

      {tool && (
        <TextToolDialog
          text={tool.text} questionId={tool.questionId} questionContext={tool.context}
          onApply={applyTool} onClose={() => setTool(null)}
        />
      )}
    </div>
  );
}
