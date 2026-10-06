import type {
  Assessment, StudentProfile, NecessityPreset, AdaptationPlan, AdaptedQuestion, ValidationResult, PlanState,
  GenerationProgress, ExportFormat, QuestionType, MeasureLibrary, Measure, EffectiveMeasure, DocumentResponse,
  DocumentStyle, ChangeLogRecord, CurriculumSummary, Curriculum, PackResponse, PackSummary, PackItem,
  ExportBundleOptions, TextTool, TextToolResponse, PlanSummary, QuestionAnalysis, MeasureKind,
} from "./types";

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5282";

class ApiError extends Error {
  status: number;
  /** A machine-readable error code the backend attaches for cases the UI should react to
   * specifically — e.g. "ai_unavailable" when Gemini is temporarily down — as opposed to an
   * arbitrary/unclassified failure the UI can only show generically. */
  code?: string;
  /** Extra detail lines (e.g. one reason per student when a class pack can't be exported). */
  reasons?: string[];
  constructor(status: number, message: string, code?: string, reasons?: string[]) {
    super(message);
    this.status = status;
    this.code = code;
    this.reasons = reasons;
  }
}

/** The backend reports known failures as JSON ({ error, code }); anything else (an unhandled
 * exception's raw text in dev, a plain-text body, an empty body) falls back to a generic message
 * instead of showing that raw text to the teacher. */
async function buildApiError(status: number, bodyText: string): Promise<ApiError> {
  try {
    const parsed = JSON.parse(bodyText) as { error?: string; code?: string; reasons?: string[] };
    if (parsed?.error) return new ApiError(status, parsed.error, parsed.code, parsed.reasons);
  } catch {
    // not JSON — fall through to the generic message below
  }
  return new ApiError(status, "Ha ocurrido un error inesperado. Inténtalo de nuevo.");
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw await buildApiError(res.status, body);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

const json = (body: unknown): RequestInit => ({ method: "POST", body: JSON.stringify(body) });
const put = (body: unknown): RequestInit => ({ method: "PUT", body: JSON.stringify(body) });

/** POSTs and saves whatever file comes back (PDF, DOCX or ZIP) using the name the server chose. */
async function download(path: string, body: unknown | undefined, fallbackName: string): Promise<string> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw await buildApiError(res.status, await res.text().catch(() => ""));
  const disposition = res.headers.get("Content-Disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/.exec(disposition);
  const fileName = match?.[1] ? decodeURIComponent(match[1]) : fallbackName;
  const blob = await res.blob();
  const blobUrl = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = blobUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(blobUrl);
  return fileName;
}

export { ApiError };

export interface ProfileInput {
  alias: string;
  grade?: number;
  measures: string[];
  accommodations: string[];
  exceptions: string[];
  settings?: Record<string, string>;
}

export interface UpdateAssessmentInput {
  title?: string;
  grade?: number;
  subject?: string;
  lockedFields?: string[];
  protectedVocabulary?: string[];
  curriculumId?: string;
  curriculumAreaId?: string;
  questions?: {
    id: string; points?: number; expectedAnswer?: string; constructTags?: string[]; type?: QuestionType;
    analysis?: QuestionAnalysis;
  }[];
}

export const api = {
  assessments: {
    list: () => request<Assessment[]>("/api/assessments"),
    get: (id: string) => request<Assessment>(`/api/assessments/${id}`),
    createFromText: (body: { title: string; text: string; grade?: number; subject?: string; language: string }) =>
      request<Assessment>("/api/assessments/text", json(body)),
    upload: async (file: File, grade: number | undefined, subject: string | undefined, language: string) => {
      const form = new FormData();
      form.append("file", file);
      if (grade !== undefined) form.append("grade", String(grade));
      if (subject) form.append("subject", subject);
      form.append("language", language);
      const res = await fetch(`${BASE_URL}/api/assessments/upload`, { method: "POST", body: form });
      if (!res.ok) throw await buildApiError(res.status, await res.text().catch(() => ""));
      return res.json() as Promise<Assessment>;
    },
    update: (id: string, body: UpdateAssessmentInput) => request<Assessment>(`/api/assessments/${id}`, put(body)),
    analyze: (id: string) => request<Assessment>(`/api/assessments/${id}/analyze`, { method: "POST" }),
    remove: (id: string) => request<void>(`/api/assessments/${id}`, { method: "DELETE" }),
    plans: (id: string) => request<PlanSummary[]>(`/api/assessments/${id}/plans`),
    packs: (id: string) => request<PackSummary[]>(`/api/assessments/${id}/packs`),
  },

  measures: {
    library: () => request<MeasureLibrary>("/api/measures"),
    createCustom: (body: { description: string; group: string; kind: MeasureKind; altersAssessedConstruct: boolean }) =>
      request<Measure>("/api/measures/custom", json(body)),
    removeCustom: (id: string) => request<void>(`/api/measures/custom/${id}`, { method: "DELETE" }),
  },

  profiles: {
    presets: () => request<NecessityPreset[]>("/api/profiles/presets"),
    effective: (body: { needs: string[]; accommodations: string[]; exceptions: string[]; schemaVersion?: number }) =>
      request<EffectiveMeasure[]>("/api/profiles/effective-measures", json(body)),
    list: () => request<StudentProfile[]>("/api/profiles"),
    get: (id: string) => request<StudentProfile>(`/api/profiles/${id}`),
    create: (body: ProfileInput) => request<StudentProfile>("/api/profiles", json(body)),
    update: (id: string, body: ProfileInput) => request<StudentProfile>(`/api/profiles/${id}`, put(body)),
    duplicate: (id: string, alias: string) =>
      request<StudentProfile>(`/api/profiles/${id}/duplicate?alias=${encodeURIComponent(alias)}`, { method: "POST" }),
    remove: (id: string) => request<void>(`/api/profiles/${id}`, { method: "DELETE" }),
  },

  curriculum: {
    list: () => request<CurriculumSummary[]>("/api/curriculum"),
    get: (id: string) => request<Curriculum>(`/api/curriculum/${id}`),
  },

  plans: {
    create: (assessmentId: string, body: {
      profileId: string; level: number; curricularChangeAuthorized?: boolean; curricularObjective?: string;
      curricularReference?: string; curricularCriteriaIds?: string[]; curricularContentIds?: string[];
    }) => request<AdaptationPlan>(`/api/assessments/${assessmentId}/plans`, json(body)),
    get: (id: string) => request<AdaptationPlan>(`/api/plans/${id}`),
    state: (id: string) => request<PlanState>(`/api/plans/${id}/state`),
    measures: (id: string) => request<Measure[]>(`/api/plans/${id}/measures`),
    generate: (id: string) => request<PlanState>(`/api/plans/${id}/generate`, { method: "POST" }),
    generateProgress: (id: string) => request<GenerationProgress | undefined>(`/api/plans/${id}/generate-progress`),
    adaptedQuestions: (id: string) => request<AdaptedQuestion[]>(`/api/plans/${id}/adapted-questions`),
    validationResults: (id: string) => request<ValidationResult[]>(`/api/plans/${id}/validation-results`),
    changelog: (id: string) => request<ChangeLogRecord[]>(`/api/plans/${id}/changelog`),
    review: (id: string, adaptedQuestionId: string, approved: boolean) =>
      request<PlanState>(`/api/plans/${id}/review`, json({ adaptedQuestionId, approved })),
    updateAdapted: (id: string, adaptedQuestionId: string, body: { adaptedText: string; supports?: string[]; kind?: "edit" | "undo"; ruleId?: string }) =>
      request<PlanState>(`/api/plans/${id}/adapted-questions/${adaptedQuestionId}`, put(body)),
    decideProposal: (id: string, adaptedQuestionId: string, accept: boolean) =>
      request<PlanState>(`/api/plans/${id}/adapted-questions/${adaptedQuestionId}/proposal`, json({ accept })),
    document: (id: string) => request<DocumentResponse>(`/api/plans/${id}/document`),
    saveDocument: (id: string, html: string, style?: DocumentStyle) =>
      request<PlanState>(`/api/plans/${id}/document`, put({ html, style })),
    logTextTool: (id: string, body: { questionId?: string | null; tool: string; before: string; after: string }) =>
      request<void>(`/api/plans/${id}/changelog/text-tool`, json(body)),
    resetDocument: (id: string) => request<PlanState>(`/api/plans/${id}/document/reset`, { method: "POST" }),
    export: (id: string, format: ExportFormat, approvedBy: string) =>
      download(
        `/api/plans/${id}/export?format=${format}&approvedBy=${encodeURIComponent(approvedBy)}`, undefined,
        `adaptaula-export.${format.toLowerCase()}`),
    exportBundle: (id: string, options: Partial<ExportBundleOptions>) =>
      download(`/api/plans/${id}/export-bundle`, options, "adaptaula-adaptacion.zip"),
  },

  packs: {
    create: (assessmentId: string, body: { title?: string; items: PackItem[] }) =>
      request<PackResponse>(`/api/assessments/${assessmentId}/packs`, json(body)),
    get: (id: string) => request<PackResponse>(`/api/packs/${id}`),
    export: (id: string, options: Partial<ExportBundleOptions>) =>
      download(`/api/packs/${id}/export`, options, "adaptaula-pack.zip"),
  },

  ai: {
    textTool: (body: { tool: TextTool; text: string; questionId?: string; questionContext?: string }) =>
      request<TextToolResponse>("/api/ai/text-tool", json(body)),
  },
};
