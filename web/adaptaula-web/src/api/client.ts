import type {
  Assessment, StudentProfile, NecessityPreset, AdaptationPlan, AdaptedQuestion,
  ValidationResult, GenerateResponse, ExportFormat, QuestionType,
} from "./types";

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(res.status, body || `Request failed: ${res.status}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export { ApiError };

export const api = {
  assessments: {
    list: () => request<Assessment[]>("/api/assessments"),
    get: (id: string) => request<Assessment>(`/api/assessments/${id}`),
    createFromText: (body: { title: string; text: string; grade: number; subject: string; language: string }) =>
      request<Assessment>("/api/assessments/text", { method: "POST", body: JSON.stringify(body) }),
    upload: async (file: File, grade: number, subject: string, language: string) => {
      const form = new FormData();
      form.append("file", file);
      form.append("grade", String(grade));
      form.append("subject", subject);
      form.append("language", language);
      const res = await fetch(`${BASE_URL}/api/assessments/upload`, { method: "POST", body: form });
      if (!res.ok) throw new ApiError(res.status, await res.text());
      return res.json() as Promise<Assessment>;
    },
    update: (id: string, body: {
      title?: string; grade?: number; subject?: string; lockedFields?: string[];
      questions?: { id: string; points?: number; expectedAnswer?: string; constructTags?: string[]; type?: QuestionType }[];
    }) => request<Assessment>(`/api/assessments/${id}`, { method: "PUT", body: JSON.stringify(body) }),
  },

  profiles: {
    presets: () => request<NecessityPreset[]>("/api/profiles/presets"),
    list: () => request<StudentProfile[]>("/api/profiles"),
    get: (id: string) => request<StudentProfile>(`/api/profiles/${id}`),
    create: (body: { alias: string; grade?: number; measures: string[]; accommodations: string[]; exceptions: string[] }) =>
      request<StudentProfile>("/api/profiles", { method: "POST", body: JSON.stringify(body) }),
    remove: (id: string) => request<void>(`/api/profiles/${id}`, { method: "DELETE" }),
  },

  plans: {
    create: (assessmentId: string, body: { profileId: string; level: number; curricularChangeAuthorized?: boolean; curricularObjective?: string }) =>
      request<AdaptationPlan>(`/api/assessments/${assessmentId}/plans`, { method: "POST", body: JSON.stringify(body) }),
    get: (id: string) => request<AdaptationPlan>(`/api/plans/${id}`),
    generate: (id: string) => request<GenerateResponse>(`/api/plans/${id}/generate`, { method: "POST" }),
    adaptedQuestions: (id: string) => request<AdaptedQuestion[]>(`/api/plans/${id}/adapted-questions`),
    validationResults: (id: string) => request<ValidationResult[]>(`/api/plans/${id}/validation-results`),
    review: (id: string, adaptedQuestionId: string, approved: boolean) =>
      request<void>(`/api/plans/${id}/review`, { method: "POST", body: JSON.stringify({ adaptedQuestionId, approved }) }),
    export: async (id: string, format: ExportFormat, approvedBy: string): Promise<void> => {
      const url = `${BASE_URL}/api/plans/${id}/export?format=${format}&approvedBy=${encodeURIComponent(approvedBy)}`;
      const res = await fetch(url, { method: "POST" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({ error: `Export failed (${res.status})` }));
        throw new ApiError(res.status, body.error ?? `Export failed (${res.status})`);
      }
      const disposition = res.headers.get("Content-Disposition") ?? "";
      const match = /filename="?([^"]+)"?/.exec(disposition);
      const fileName = match?.[1] ?? `adaptaula-export.${format.toLowerCase()}`;
      const blob = await res.blob();
      const blobUrl = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = blobUrl;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(blobUrl);
    },
  },
};
