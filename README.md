# Aula Adaptada

Sube una prueba (DOCX, PDF con texto, o texto pegado) y genera versiones accesibles y
personalizadas para distintos perfiles de alumnado (dislexia, TDAH, TDL/TEL, TEA, adaptación
curricular autorizada por el docente, altas capacidades), preservando el contenido, los
criterios, la respuesta correcta y la puntuación total salvo que el docente autorice
explícitamente un cambio curricular. La herramienta propone; el docente revisa y aprueba.

Basado en la especificación funcional/técnica y la matriz de reglas de adaptación acordadas para el proyecto.

## Arquitectura

- **Backend**: ASP.NET Core Web API (.NET 7, C#), SQLite vía EF Core.
  - `AdaptAula.Domain` — modelos de dominio.
  - `AdaptAula.RulesEngine` — catálogo de reglas atómicas (transcrito de la matriz) + resolutor determinista.
  - `AdaptAula.Validation` — validador de seguridad pedagógica (spec §11).
  - `AdaptAula.Infrastructure` — ingesta de documentos (DOCX/PDF/texto), cliente de IA (Gemini), exportación (DOCX/PDF).
  - `AdaptAula.Api` — endpoints REST, orquestación del pipeline.
- **Frontend**: React + TypeScript + Vite, con el look & feel de AGZ Labs (`web/adaptaula-web`).

La IA **nunca decide qué adaptaciones aplicar** — eso lo resuelve `PlanResolver` de forma
determinista. La IA solo reescribe texto según una lista de reglas ya resueltas, y su esquema de
salida no incluye ni puntos ni la respuesta esperada, así que no puede alterarlos.

## Puesta en marcha

### Backend

```bash
dotnet build AdaptAula.sln
dotnet run --project src/AdaptAula.Api --urls http://localhost:5080
```

La base de datos SQLite se crea automáticamente en `src/AdaptAula.Api/App_Data/adaptaula.db`.

Configura la clave de Gemini (gratuita en https://aistudio.google.com/apikey) para que el paso
de generación funcione; sin ella, el pipeline sigue funcionando pero mantiene el texto original
de cada pregunta (falla de forma segura, nunca inventa ni bloquea el resto del flujo). Cualquiera
de estas opciones sirve:

```bash
# variable de entorno
export Gemini__ApiKey="tu-clave"

# o en src/AdaptAula.Api/appsettings.Development.json (no se versiona)
{ "Gemini": { "ApiKey": "tu-clave" } }
```

### Frontend

```bash
cd web/adaptaula-web
npm install
npm run dev
```

Sirve en `http://localhost:5173` y ya está configurado (CORS + `VITE_API_BASE_URL`) para hablar
con el backend en `http://localhost:5080`.

### Tests

```bash
dotnet test tests/AdaptAula.Tests/AdaptAula.Tests.csproj
```

## Despliegue en agzlabs.com

Aula Adaptada vive como subweb de [AGZ Labs](https://agzlabs.com), en dos carpetas hermanas del
mismo dominio (para evitar que la app .NET y el sitio estático se pisen):

- **Frontend** (`web/adaptaula-web/dist`, tras `npm run build`) → sube el contenido de `dist/` a
  `public_html/aula-adaptada/` por FTP. Vite ya compila con `base: '/aula-adaptada/'` y
  `VITE_API_BASE_URL` apunta a `https://agzlabs.com/aula-adaptada-api/api` (ver
  `web/adaptaula-web/.env.production`). Incluye un `.htaccess` con fallback a `index.html` para
  que las rutas internas de React Router (`basename="/aula-adaptada"`) funcionen al refrescar o
  enlazar directamente.
- **Backend** (`dotnet publish src/AdaptAula.Api/AdaptAula.Api.csproj -c Release -o publish/aula-adaptada-api`)
  → sube el contenido de esa carpeta (incluye `web.config` con el módulo ASP.NET Core para IIS) a
  la carpeta que tu panel de hosting asigne para una "aplicación .NET" en la ruta
  `/aula-adaptada-api`. CORS en `Program.cs` ya permite `https://agzlabs.com`.
  Recuerda configurar `Gemini__ApiKey` en el entorno del hosting (o en un
  `appsettings.Production.json` no versionado) — sin ella el pipeline sigue funcionando pero no
  reescribe texto.
- Si el panel solo permite montar la app .NET en una ruta distinta a `/aula-adaptada-api`, cambia
  `VITE_API_BASE_URL` en `.env.production` a la ruta real y repite `npm run build` antes de subir.

## Alcance de esta primera versión (MVP1)

- Formatos: texto pegado, DOCX, PDF con capa de texto (no escaneado/OCR).
- Perfiles: dislexia, TDAH, TDL/TEL, TEA, adaptación curricular controlada por el docente, altas capacidades.
- Comparador original/adaptado, validador básico, exportación a DOCX y PDF.
- Perfiles de alumnado por alias — nunca nombre real ni diagnóstico clínico.

Fuera de alcance por ahora (roadmap): OCR de documentos escaneados, hipoacusia/baja
visión/discapacidad motora en la UI, adaptación masiva por clase, integración con
Drive/OneDrive, autenticación multi-centro.
