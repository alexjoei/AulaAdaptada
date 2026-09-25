import { Routes, Route } from "react-router-dom";
import Layout from "./components/Layout";
import Dashboard from "./pages/Dashboard";
import Upload from "./pages/Upload";
import Analysis from "./pages/Analysis";
import AdaptationSelector from "./pages/AdaptationSelector";
import PlanWorkspace from "./pages/PlanWorkspace";
import Generation from "./pages/Generation";
import Profiles from "./pages/Profiles";

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<Dashboard />} />
        <Route path="/upload" element={<Upload />} />
        <Route path="/assessments/:id/analysis" element={<Analysis />} />
        <Route path="/assessments/:id/adapt" element={<AdaptationSelector />} />
        <Route path="/plans/:id" element={<PlanWorkspace />} />
        <Route path="/plans/:id/export" element={<Generation />} />
        <Route path="/profiles" element={<Profiles />} />
      </Route>
    </Routes>
  );
}
