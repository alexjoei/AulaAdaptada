import { NavLink, Outlet } from "react-router-dom";

function BrandMark() {
  return (
    <span className="brand-mark" aria-hidden="true">
      <svg width="22" height="22" viewBox="0 0 24 24">
        <circle cx="12" cy="12" r="9" fill="none" stroke="#7c3aed" strokeWidth="1.6" />
        <path d="M8 12h8M12 8v8" stroke="#06b6d4" strokeWidth="1.6" strokeLinecap="round" />
      </svg>
    </span>
  );
}

export default function Layout() {
  return (
    <>
      <nav className="nav">
        <div className="wrap nav-inner">
          <NavLink to="/" className="nav-brand" style={{ textDecoration: "none" }}>
            <BrandMark />
            Aula Adaptada
          </NavLink>
          <ul className="nav-links">
            <li><NavLink to="/" end className={({ isActive }) => `nav-link${isActive ? " active" : ""}`}>Panel</NavLink></li>
            <li><NavLink to="/upload" className={({ isActive }) => `nav-link${isActive ? " active" : ""}`}>Nueva adaptación</NavLink></li>
            <li><NavLink to="/profiles" className={({ isActive }) => `nav-link${isActive ? " active" : ""}`}>Perfiles</NavLink></li>
          </ul>
          <NavLink to="/upload" className="btn btn-sm">Nueva adaptación</NavLink>
        </div>
      </nav>

      <main>
        <Outlet />
      </main>

      <footer>
        <div className="wrap">
          <p className="footer-brand">Aula Adaptada <span className="sep">·</span> un proyecto de AGZ Labs</p>
          <p className="legal">
            Herramienta de apoyo docente. No diagnostica ni sustituye la decisión profesional del centro.
          </p>
        </div>
      </footer>
    </>
  );
}
