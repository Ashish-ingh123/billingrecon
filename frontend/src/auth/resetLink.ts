/** What the link of a forgotten password carries after its `#` (ADR 0061, amended). */
export interface ResetLink {
  token: string;
  hospital: string;
}

/**
 * Reads `#token=…&hospital=…`, the fragment of the link in the mail. Null for anything else: a page opened without a
 * link, or with one a mail program cut short.
 */
export function resetLinkFrom(hash: string): ResetLink | null {
  const parts = new URLSearchParams(hash.startsWith('#') ? hash.slice(1) : hash);
  const token = parts.get('token')?.trim() ?? '';
  const hospital = parts.get('hospital')?.trim() ?? '';

  return /^[A-Za-z0-9_-]{20,100}$/.test(token) && hospital !== '' && hospital.length <= 64 ? { token, hospital } : null;
}
