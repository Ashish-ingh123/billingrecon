/**
 * A refusal or a failure of the API, as the screens show it (ADR 0011, ADR 0038): the API answers every error as an
 * RFC 9457 ProblemDetails with a stable `errorCode`, a message fit to show (`detail`), the `correlationId` to quote to
 * support, and for a 400 the messages per field. An answer that is not one (a proxy's error page, no answer at all) is
 * described as plainly.
 */
export interface ApiProblem {
  /** The HTTP status; 0 when the server could not be reached. */
  status: number;
  /** The API's stable code (`invalid_credentials`, `validation_failed`); null when the answer had none. */
  errorCode: string | null;
  title: string;
  detail: string | null;
  /** The messages per field, keyed by the JSON name of the field (`tenant`, `pageSize`) or a header's name. */
  fieldErrors: Record<string, string[]>;
  /** The reference to quote to support. */
  correlationId: string | null;
  /** How long to wait before trying again (a 429's `Retry-After`), in seconds. */
  retryAfterSeconds: number | null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

function fieldErrorsOf(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) {
    return {};
  }

  const errors: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      const strings = messages.filter((message): message is string => typeof message === 'string');
      if (strings.length > 0) {
        errors[field] = strings;
      }
    }
  }

  return errors;
}

function fallbackTitle(status: number): string {
  if (status >= 500) {
    return 'Something went wrong on the server. Try again in a moment.';
  }

  return `The request could not be completed (HTTP ${status}).`;
}

/** The problem an error answer describes. `body` is what the client read from it: parsed JSON, or the raw text. */
export function problemFrom(response: Response, body: unknown): ApiProblem {
  const retryAfter = Number.parseInt(response.headers.get('Retry-After') ?? '', 10);
  const problem = isRecord(body) ? body : {};

  return {
    status: response.status,
    errorCode: text(problem.errorCode),
    title: text(problem.title) ?? fallbackTitle(response.status),
    detail: text(problem.detail),
    fieldErrors: fieldErrorsOf(problem.errors),
    correlationId: text(problem.correlationId) ?? response.headers.get('X-Correlation-Id'),
    retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : null,
  };
}

/** The problem of a request that got no answer: the network, the proxy or the API is down. */
export function unreachableProblem(): ApiProblem {
  return {
    status: 0,
    errorCode: null,
    title: 'The server could not be reached. Check the connection and try again.',
    detail: null,
    fieldErrors: {},
    correlationId: null,
    retryAfterSeconds: null,
  };
}
