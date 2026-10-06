export type QuestionType = "OpenText" | "ShortAnswer" | "MultipleChoice" | "Classification" | "FillInTheBlank" | "Matching";
export type ResponseMode = "Written" | "Oral" | "Keyboard" | "Selection" | "Combined";
export type ValidationSeverity = "Error" | "Warning" | "Review";
export type ValidationCode =
  | "PointsChanged" | "AnswerChanged" | "ContentDropped" | "ReadingConstructBypassed"
  | "SpellingConstructBypassed" | "HintRevealsAnswer" | "DifficultyReduced"
  | "CurricularChange" | "LowExtractionConfidence" | "UnsupportedConflict"
  | "ProtectedVocabularyMissing" | "QuestionCountChanged" | "LanguageChanged" | "NumberingChanged"
  | "AnswerSpaceMissing" | "ExcessiveHints" | "CriteriaCoverage" | "CognitiveDemandChanged"
  | "ImageMayRevealAnswer" | "ProposalPending" | "QuestionMissing";
export type PlanStatus = "Draft" | "Planned" | "Generated" | "NeedsTeacherReview" | "Approved" | "Exported";
export type RuleSource = "TeacherLock" | "IndividualMeasure" | "ConstructProtection" | "NecessityPreset" | "GlobalStyle";
export type ExportFormat = "Docx" | "Pdf";
export type SemaphoreLevel = "Green" | "Orange" | "Red";
export type MeasureClassification = "Recommended" | "Optional" | "RequiresTeacherDecision" | "NotRecommended";
export type MeasureGroupKey =
  | "presentation" | "reading" | "writing" | "language" | "attention" | "math" | "time" | "response" | "visual" | "evaluation";
export type MeasureKind = "Text" | "Support" | "Style" | "Logistics";
export type RiskLevel = "Low" | "Medium" | "High" | "Critical";
export type TeacherDecision = "Pending" | "Accepted" | "Rejected" | "Edited";

export interface QuestionAnalysis {
  content: string;
  skill: string;
  cognitiveDemand: string;
  linguisticDemand: string;
  readingLoad: string;
  writingLoad: string;
  executiveLoad: string;
  criteriaIds: string[];
  contentIds: string[];
  source: string;
  confirmed: boolean;
}

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
  analysis: QuestionAnalysis | null;
}

export interface Section {
  id: string;
  assessmentId: string;
  title: string;
  order: number;
  stimulusText: string | null;
  assetRefs: string[];
  questions: Question[];
}

export interface Assessment {
  id: string;
  title: string;
  grade: number | null;
  subject: string | null;
  language: string;
  totalPoints: number;
  sourceFileName: string | null;
  createdAt: string;
  extractionConfidence: number | null;
  lockedFields: string[];
  imageDataUris: string[];
  protectedVocabulary: string[];
  curriculumId: string | null;
  curriculumAreaId: string | null;
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
  settings: Record<string, string>;
  schemaVersion: number;
  reviewDate: string;
}

export interface NeedMeasure {
  ruleId: string;
  classification: MeasureClassification;
}

export interface NecessityPreset {
  key: string;
  displayName: string;
  description: string;
  hidden: boolean;
  ruleIds: string[];
  measures: NeedMeasure[];
}

export interface MeasureParameter {
  key: string;
  label: string;
  type: string;
  default: string;
  unit: string;
  choices: string[];
}

export interface Measure {
  id: string;
  description: string;
  group: string;
  category: string;
  kind: MeasureKind;
  applyMode: string;
  minLevel: number;
  riskLevel: RiskLevel;
  blockedByConstructTags: string[];
  requiresTeacherReview: boolean;
  altersAssessedConstruct: boolean;
  rationale: string;
  style: Record<string, string>;
  parameters: MeasureParameter[];
  isCustom: boolean;
}

export interface MeasureLibrary {
  groups: { key: MeasureGroupKey; label: string }[];
  needs: NecessityPreset[];
  measures: Measure[];
}

export interface EffectiveMeasure {
  ruleId: string;
  classification: MeasureClassification;
  enabled: boolean;
  origin: "need" | "individual";
  needKeys: string[];
  classificationConflict: boolean;
  warning: string | null;
}

export interface ResolvedRule {
  ruleId: string;
  source: RuleSource;
  applied: boolean;
  reason: string;
  proposalOnly: boolean;
}

export interface DocumentStyle {
  fontFamily: string;
  fontSizePt: number;
  lineSpacing: number;
  letterSpacingPt: number;
  wordSpacingPt: number;
  paragraphSpacingPt: number;
  align: string;
  marginMm: number;
  pageBreakPerQuestion: boolean;
  highContrast: boolean;
  checklistSupports: boolean;
  answerSpaceFactor: number;
  ruledAnswerLines: boolean;
  gridAnswerSpace: boolean;
  hideDecorativeImages: boolean;
}

export interface AdaptationPlan {
  id: string;
  assessmentId: string;
  profileId: string;
  level: number;
  status: PlanStatus;
  isCurricularChange: boolean;
  curricularObjective: string | null;
  curricularReference: string | null;
  curricularCriteriaIds: string[];
  curricularContentIds: string[];
  locks: string[];
  resolvedRulesByQuestion: Record<string, ResolvedRule[]>;
  warnings: string[];
  style: DocumentStyle;
  profileSettings: Record<string, string>;
  documentEditedAt: string | null;
  packId: string | null;
  createdAt: string;
}

export interface PlanSummary {
  id: string;
  profileId: string;
  alias: string;
  level: number;
  status: PlanStatus;
  isCurricularChange: boolean;
  createdAt: string;
  packId: string | null;
}

export interface ChangeLogEntry {
  ruleId: string;
  category: string;
  description: string;
  reason: string;
  before: string;
  after: string;
}

export interface QuestionProposal {
  proposedText: string;
  ruleIds: string[];
  reason: string;
  status: "Pending" | "Accepted" | "Rejected";
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
  proposal: QuestionProposal | null;
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

export interface QuestionSemaphore {
  questionId: string;
  level: SemaphoreLevel;
  reasons: string[];
}

export interface SemaphoreReport {
  overall: SemaphoreLevel;
  overallReasons: string[];
  questions: QuestionSemaphore[];
}

export interface PlanState {
  adaptedQuestions: AdaptedQuestion[];
  validationResults: ValidationResult[];
  canExport: boolean;
  semaphore: SemaphoreReport;
  documentOutdated: boolean;
}

export interface DocumentResponse {
  html: string;
  style: DocumentStyle;
  edited: boolean;
  title: string;
  language: string;
}

export interface ChangeLogRecord {
  id: string;
  planId: string;
  questionId: string | null;
  kind: string;
  ruleId: string;
  before: string;
  after: string;
  reason: string;
  risk: RiskLevel;
  decision: TeacherDecision;
  actor: string;
  at: string;
}

export interface GenerationProgress {
  current: number;
  total: number;
}

// ---- curriculum
export interface CurriculumCycle { id: string; name: string; grades: number[] }
export interface KeyCompetence { id: string; name: string; descriptors: { id: string; text: string }[] }
export interface SpecificCompetence { id: string; number: number; text: string; keyDescriptors: string[] }
export interface EvaluationCriterion { id: string; cycle: string; competenceId: string; text: string }
export interface BasicContent { id: string; cycle: string; block: string; text: string }
export interface CurriculumArea {
  id: string; name: string; competences: SpecificCompetence[]; criteria: EvaluationCriterion[]; contents: BasicContent[];
}
export interface Curriculum {
  id: string; ccaa: string; stage: string; legalBasis: string; verified: boolean; note: string;
  cycles: CurriculumCycle[]; keyCompetences: KeyCompetence[]; areas: CurriculumArea[];
}
export interface CurriculumSummary {
  id: string; ccaa: string; stage: string; legalBasis: string; verified: boolean; note: string;
  cycles: CurriculumCycle[]; areas: { id: string; name: string }[];
}

// ---- packs
export interface PackPlanState {
  planId: string;
  profileId: string;
  alias: string;
  status: PlanStatus;
  generated: boolean;
  semaphore: SemaphoreLevel | null;
  errors: number;
  reviews: number;
  pendingProposals: number;
}
export interface PackResponse { id: string; assessmentId: string; title: string; plans: PackPlanState[] }
export interface PackSummary { id: string; title: string; students: number; createdAt: string }

export interface PackItem {
  profileId: string;
  level: number;
  curricularChangeAuthorized: boolean;
  curricularObjective?: string;
  curricularReference?: string;
  curricularCriteriaIds?: string[];
  curricularContentIds?: string[];
}

export interface ExportBundleOptions {
  pdf: boolean;
  docx: boolean;
  includeOriginal: boolean;
  teacherSheet: boolean;
  answerKey: boolean;
  changeLog: boolean;
  accessibleHtml: boolean;
  approvedBy?: string;
}

// ---- text tools
export type TextTool =
  | "Shorten" | "SimplifySyntax" | "SplitIntoSteps" | "HighlightKeywords" | "AddExample"
  | "WordBank" | "Hint" | "Checklist" | "Organizer" | "ReadAloud";

export interface TextToolResponse {
  original: string;
  proposal: string;
  note: string;
  warnings: string[];
}
