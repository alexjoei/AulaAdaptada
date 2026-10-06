import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { MeasureLibrary } from "../api/types";

let cached: Promise<MeasureLibrary> | null = null;

/** The measure library (groups, measures, needs) as served by the backend. Cached for the session; `reload` re-fetches it
 * after the teacher defines a custom measure. */
export function useLibrary() {
  const [library, setLibrary] = useState<MeasureLibrary | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    cached ??= api.measures.library();
    cached.then(setLibrary).catch(() => { cached = null; setError("No se pudo cargar la biblioteca de medidas."); });
  }, []);

  function reload() {
    cached = api.measures.library();
    cached.then(setLibrary).catch(() => setError("No se pudo recargar la biblioteca de medidas."));
  }

  return { library, error, reload };
}
