export type DiffToken = { text: string; type: "same" | "added" | "removed" };

/** Word-level diff (Wagner–Fischer/LCS) between two texts, splitting on whitespace so the
 * separators themselves are preserved as "same" tokens when unchanged. */
export function diffWords(oldText: string, newText: string): DiffToken[] {
  const a = oldText.split(/(\s+)/).filter((t) => t.length > 0);
  const b = newText.split(/(\s+)/).filter((t) => t.length > 0);
  const n = a.length;
  const m = b.length;

  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = a[i] === b[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }

  const tokens: DiffToken[] = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      tokens.push({ text: a[i], type: "same" });
      i++; j++;
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      tokens.push({ text: a[i], type: "removed" });
      i++;
    } else {
      tokens.push({ text: b[j], type: "added" });
      j++;
    }
  }
  while (i < n) { tokens.push({ text: a[i], type: "removed" }); i++; }
  while (j < m) { tokens.push({ text: b[j], type: "added" }); j++; }
  return tokens;
}
