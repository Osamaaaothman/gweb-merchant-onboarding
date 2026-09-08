// Every log line in this codebase must route through redact() before it reaches
// stdout. See docs/04-SECURITY-RULES.md §2 for the authoritative list of what must
// never appear in logs.

const SENSITIVE_KEY_PATTERNS: RegExp[] = [
  /government.?id/i,
  /\bssn\b/i,
  /tax.?id/i,
  /\bein\b/i,
  /date.?of.?birth/i,
  /\bdob\b/i,
  /bank.?account/i,
  /routing.?number/i,
  /account.?number/i,
  /\bsecret/i,
  /\btoken/i,
  /password/i,
  /api.?key/i,
  /presigned.?url/i,
  /document.?body/i,
  /document.?content/i,
  /\bprompt/i,
];

// Matches an S3 pre-signed URL by its query-string signature, so a credential-bearing
// URL is caught even when logged under an innocuous key name like "uploadUrl".
const PRESIGNED_URL_PATTERN = /X-Amz-Signature=|X-Amz-Credential=/i;

const REDACTED = "[REDACTED]";
const CIRCULAR = "[CIRCULAR]";

/**
 * Deep-clones `value`, replacing any value under a sensitive key (or any string that
 * looks like a pre-signed URL) with a fixed redaction marker. Safe against circular
 * references. Non-object values that aren't sensitive strings pass through unchanged.
 */
export function redact<T>(value: T, seen: WeakSet<object> = new WeakSet()): T {
  if (typeof value === "string") {
    return (PRESIGNED_URL_PATTERN.test(value) ? REDACTED : value) as T;
  }

  if (value === null || typeof value !== "object") {
    return value;
  }

  const obj = value as object;
  if (seen.has(obj)) {
    return CIRCULAR as T;
  }
  seen.add(obj);

  if (Array.isArray(value)) {
    return value.map((item) => redact(item, seen)) as T;
  }

  const result: Record<string, unknown> = {};
  for (const [key, val] of Object.entries(value as Record<string, unknown>)) {
    result[key] = isSensitiveKey(key) ? REDACTED : redact(val, seen);
  }
  return result as T;
}

function isSensitiveKey(key: string): boolean {
  return SENSITIVE_KEY_PATTERNS.some((pattern) => pattern.test(key));
}
