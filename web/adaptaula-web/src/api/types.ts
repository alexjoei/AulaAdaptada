export type QuestionType = "OpenText" | "ShortAnswer" | "MultipleChoice" | "Classification" | "FillInTheBlank" | "Matching";
export type ResponseMode = "Written" | "Oral" | "Keyboard" | "Selection" | "Combined";
export type ValidationSeverity = "Error" | "Warning" | "Review";
export type ValidationCode =
  | "PointsChanged" | "AnswerChanged" | "ContentDropped" | "ReadingConstructBypassed"
  | "SpellingConstructBypassed" | "HintRevealsAnswer" | "DifficultyReduced"
  | "CurricularChange" | "LowExtractionConfidence" | "UnsupportedConflict";
export type PlanStatus = "Draft" | "Planned" | "Generated" | "NeedsTeacherReview" | "Approved" | "Exported";
export type RuleSource = "TeacherLock" | "IndividualMeasure" | "ConstructProtection" | "NecessityPreset" | "GlobalStyle";
export type ExportFormat = "Docx" | "Pdf";

export interface Question {
  id: string;
  sectionId: string;
  order: number;
  originalText: string;
  type: QuestionType;
  points: number;
  expectedAnswer: string | null;
  constructTags: string[];
  options: string[];
  assetRefs: string[];
}

export interface Section {
  id: string;
  assessmentId: string;
  title: string;
  order: number;
  questions: Question[];
}

export interface Assessment {
  id: string;
  title: string;
  grade: number;
  subject: string;
  language: string;
  totalPoints: number;
  sourceFileName: string | null;
  createdAt: string;
  extractionConfidence: number | null;
  lockedFields: string[];
  sections: Section[];
}

export interface StudentProfile {
  id: string;
  alias: string;
  grade: number | null;
  curricularLevelOverride: string | null;
  measures: string[];
  accommodations: string[];
  exceptions: string[];
  reviewDate: string;
}

export interface NecessityPreset {
  key: string;
  displayName: string;
  ruleIds: string[];
}

export interface ResolvedRule {
  ruleId: string;
  source: RuleSource;
  applied: boolean;
  reason: string;
}

export interface AdaptationPlan {
  id: string;
  assessmentId: string;
  profileId: string;
  level: number;
  status: PlanStatus;
  isCurricularChange: boolean;
  curricularObjective: string | null;
  locks: string[];
  resolvedRulesByQuestion: Record<string, ResolvedRule[]>;
  warnings: string[];
  createdAt: string;
}

export interface ChangeLogEntry {
  ruleId: string;
  category: string;
  description: string;
  reason: string;
}

export interface AdaptedQuestion {
  id: string;
  planId: string;
  questionId: string;
  adaptedText: string;
  responseMode: ResponseMode;
  supports: string[];
  points: number;
  changeLog: ChangeLogEntry[];
  teacherApproved: boolean;
}

export interface ValidationResult {
  id: string;
  planId: string;
  severity: ValidationSeverity;
  code: ValidationCode;
  questionId: string | null;
  message: string;
  originalValue: string | null;
  adaptedValue: string | null;
  requiresReview: boolean;
}

export interface GenerateResponse {
  adaptedQuestions: AdaptedQuestion[];
  validationResults: ValidationResult[];
  canExport: boolean;
}
