import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { Assessment } from "../api/types";
import { Eyebrow, EmptyState } from "../components/ui";

export default function Dashboard() {
  const [assessments, setAssessments] = useState<Assessment[] | null>(null);

  useEffect(() => {
    api.assessments.list().then(setAssessments).catch(() => setAssessments([]));
  }, []);

  return (
    <div className="page">
      <div className="wrap">
        <Eyebrow>Panel</Eyebrow>
        <h1 style={{ fontSize: 32, marginBottom: 8 }}>
          Tus <span className="grad-text">pruebas</span> y adaptaciones
        </h1>
        <p className="muted" style={{ marginBottom: 32, maxWidth: 640 }}>
          Sube una prueba, elige un perfil de alumno y genera versiones accesibles sin cambiar
          lo que se evalúa. Tú revisas y apruebas cada cambio antes de exportar.
        </p>

        <div className="row" style={{ marginBottom: 32 }}>
          <Link to="/upload" className="btn">Nueva adaptación</Link>
          <Link to="/profiles" className="btn btn-outline">Gestionar perfiles</Link>
        </div>

        {assessments === null && <p className="muted">Cargando…</p>}

        {assessments !== null && assessments.length === 0 && (
          <EmptyState
            title="Todavía no hay pruebas"
            description="Sube tu primer examen o ficha en PDF, DOCX o pegando el texto para empezar."
          />
        )}

        {assessments !== null && assessments.length > 0 && (
          <div className="card-grid cols-2">
            {assessments.map((a) => (
              <Link key={a.id} to={`/assessments/${a.id}/analysis`} className="card-panel" style={{ textDecoration: "none" }}>
                <h3 style={{ fontSize: 18, marginBottom: 8 }}>{a.title}</h3>
                <p className="muted" style={{ fontSize: 14 }}>
                  {a.subject} · {a.grade}º · {a.totalPoints} puntos · {a.sections.reduce((n, s) => n + s.questions.length, 0)} preguntas
                </p>
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
