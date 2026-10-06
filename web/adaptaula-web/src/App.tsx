import { lazy, Suspense } from "react";
import { Routes, Route } from "react-router-dom";
import Layout from "./components/Layout";
import Dashboard from "./pages/Dashboard";
import Upload from "./pages/Upload";
import Analysis from "./pages/Analysis";
import AdaptationSelector from "./pages/AdaptationSelector";
import PlanWorkspace from "./pages/PlanWorkspace";
import Generation from "./pages/Generation";
import Profiles from "./pages/Profiles";
// The visual editor (TipTap) is the heaviest part of the app: load it only when a teacher opens it.
const PlanEditor = lazy(() => import("./pages/PlanEditor"));
import PackPage from "./pages/PackPage";
import History from "./pages/History";

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<Dashboard />} />
        <Route path="/upload" element={<Upload />} />
        <Route path="/assessments/:id/analysis" element={<Analysis />} />
        <Route path="/assessments/:id/adapt" element={<AdaptationSelector />} />
        <Route path="/plans/:id" element={<PlanWorkspace />} />
        <Route path="/plans/:id/edit" element={<Suspense fallback={<div className="page wrap"><p className="muted">Abriendo el editor…</p></div>}><PlanEditor /></Suspense>} />
        <Route path="/plans/:id/export" element={<Generation />} />
        <Route path="/plans/:id/history" element={<History />} />
        <Route path="/packs/:id" element={<PackPage />} />
        <Route path="/profiles" element={<Profiles />} />
      </Route>
    </Routes>
  );
}
