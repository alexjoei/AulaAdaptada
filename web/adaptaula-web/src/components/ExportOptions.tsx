import type { ExportBundleOptions } from "../api/types";

export const DEFAULT_OPTIONS: ExportBundleOptions = {
  pdf: true, docx: true, includeOriginal: true, teacherSheet: true, answerKey: false, changeLog: false, accessibleHtml: false,
};

interface Props {
  value: ExportBundleOptions;
  onChange: (v: ExportBundleOptions) => void;
  /** A pack also carries the original test; a single student's bundle does not. */
  showOriginal?: boolean;
}

/** What goes into the download (V2 §20): print-ready PDF + editable Word, and optionally the accessible digital version,
 * the teacher sheet, the answer key/rubric and the change history. */
export default function ExportOptions({ value, onChange, showOriginal = false }: Props) {
  const set = (key: keyof ExportBundleOptions) => (e: React.ChangeEvent<HTMLInputElement>) => onChange({ ...value, [key]: e.target.checked });

  return (
    <div className="stack" data-testid="export-options">
      <div>
        <div className="measure-group-title">Formatos</div>
        <div className="row">
          <label className="checkbox-row"><input type="checkbox" checked={value.pdf} onChange={set("pdf")} /> PDF listo para imprimir</label>
          <label className="checkbox-row"><input type="checkbox" checked={value.docx} onChange={set("docx")} /> Word editable</label>
          <label className="checkbox-row"><input type="checkbox" checked={value.accessibleHtml} onChange={set("accessibleHtml")} /> Versión digital accesible</label>
        </div>
      </div>
      <div>
        <div className="measure-group-title">Documentos para el docente</div>
        <div className="row">
          {showOriginal && <label className="checkbox-row"><input type="checkbox" checked={value.includeOriginal} onChange={set("includeOriginal")} /> Prueba original</label>}
          <label className="checkbox-row"><input type="checkbox" checked={value.teacherSheet} onChange={set("teacherSheet")} /> Hoja docente (resumen de cambios)</label>
          <label className="checkbox-row"><input type="checkbox" checked={value.answerKey} onChange={set("answerKey")} /> Rúbrica / solucionario</label>
          <label className="checkbox-row"><input type="checkbox" checked={value.changeLog} onChange={set("changeLog")} /> Historial de cambios</label>
        </div>
      </div>
    </div>
  );
}
