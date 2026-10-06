import { diffWords } from "./diff";

/** One contiguous change between the original and the adapted text — what the comparator lets the teacher click, explain and undo (V2 §15). */
export interface Hunk {
  id: number;
  /** What the original had here (may be empty for a pure addition). */
  removed: string;
  /** What the adapted text has here (may be empty for a pure deletion). */
  added: string;
}

export type Segment = { kind: "same"; text: string } | { kind: "hunk"; hunk: Hunk };

/** Groups the word diff into segments. Whitespace sitting between two changed runs belongs to the change (so "a b" → "x y" is one hunk, not two). */
export function buildSegments(original: string, adapted: string): Segment[] {
  const tokens = diffWords(original, adapted);
  const segments: Segment[] = [];
  let nextId = 0;
  let current: Hunk | null = null;
  let pendingSpace = "";

  const flushSame = (text: string) => {
    if (!text) return;
    const last = segments[segments.length - 1];
    if (last?.kind === "same") last.text += text;
    else segments.push({ kind: "same", text });
  };

  for (const token of tokens) {
    if (token.type === "same") {
      if (current && /^\s+$/.test(token.text)) {
        pendingSpace += token.text; // might be interior to the hunk — decided when the next token shows up
        continue;
      }
      if (current) { segments.push({ kind: "hunk", hunk: current }); current = null; }
      flushSame(pendingSpace + token.text);
      pendingSpace = "";
    } else {
      if (!current) {
        current = { id: nextId++, removed: "", added: "" };
        flushSame(pendingSpace);
        pendingSpace = "";
      } else if (pendingSpace) {
        current.removed += pendingSpace;
        current.added += pendingSpace;
        pendingSpace = "";
      }
      if (token.type === "removed") current.removed += token.text;
      else current.added += token.text;
    }
  }
  if (current) segments.push({ kind: "hunk", hunk: current });
  flushSame(pendingSpace);
  return segments;
}

/** The adapted text with ONE change reverted to the original wording and every other change kept. */
export function undoHunk(segments: Segment[], hunkId: number): string {
  return segments.map((s) => (s.kind === "same" ? s.text : s.hunk.id === hunkId ? s.hunk.removed : s.hunk.added)).join("");
}

export function hunksOf(segments: Segment[]): Hunk[] {
  return segments.flatMap((s) => (s.kind === "hunk" ? [s.hunk] : []));
}
