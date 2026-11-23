import { problemFrom, unreachableProblem, type ApiProblem } from './problem';

/** A refusal or failure of the API, as an error a query or a mutation can throw (ADR 0038). */
export class ApiError extends Error {
  readonly problem: ApiProblem;
  /** The HTTP status, 0 when there was no answer; read by the query cache to retry faults and never refusals. */
  readonly status: number;

  constructor(problem: ApiProblem) {
    super(problem.detail ?? problem.title);
    this.name = 'ApiError';
    this.problem = problem;
    this.status = problem.status;
  }
}

/**
 * The data of an answer of the API, or an {@link ApiError} with what the API said, or that nothing answered. Every
 * screen reads and changes records through this, so a refusal reaches it in one shape.
 */
export async function answer<T>(request: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  let result: { data?: T; error?: unknown; response: Response };
  try {
    result = await request;
  } catch {
    throw new ApiError(unreachableProblem());
  }

  if (result.data === undefined) {
    throw new ApiError(problemFrom(result.response, result.error));
  }

  return result.data;
}

/** What a thrown error says, as the error component shows it. */
export function problemOf(error: unknown): ApiProblem {
  return error instanceof ApiError ? error.problem : unreachableProblem();
}

/** Whether an error is the API saying the record changed since it was read (409 `concurrency_conflict`, BR-804). */
export function isConflict(error: unknown): boolean {
  return error instanceof ApiError && error.problem.errorCode === 'concurrency_conflict';
}

/**
 * The `If-Match` header of a change: the record's version as the screen read it, as the strong ETag the API answers
 * (`docs/api.md` section 4, ADR 0039 point 2).
 */
export function ifMatch(version: string): { 'If-Match': string } {
  return { 'If-Match': `"${version}"` };
}
