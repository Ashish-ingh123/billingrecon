import createClient from 'openapi-fetch';
import type { paths } from './schema';

/**
 * The typed client of the API (ADR 0038, point 4): its paths, parameters, bodies and answers are the types generated from
 * the API's own OpenAPI document (`src/api/schema.d.ts`, from `src/api/openapi.json`).
 */
export type ApiClient = ReturnType<typeof createClient<paths>>;

export interface ApiClientOptions {
  /** The origin the app was loaded from: in development Vite forwards `/api` to the API, in production the proxy does. */
  baseUrl: string;
  /** The token of the signed-in user, if any. */
  getToken: () => string | undefined;
  /** Called when the API refuses a token the request carried: it expired, or was not this API's. */
  onUnauthorized: () => void;
  /** For tests: what sends a request. */
  fetch?: (request: Request) => Promise<Response>;
  /** For tests: a new correlation id. */
  newCorrelationId?: () => string;
}

/**
 * Every request carries the user's token and an `X-Correlation-Id` of its own, which the API writes into every log event
 * and audit record of the request (BR-703) and answers in its errors, so a reference a user quotes leads to the request.
 */
export function createApiClient(options: ApiClientOptions): ApiClient {
  const client = createClient<paths>({
    baseUrl: options.baseUrl,
    ...(options.fetch === undefined ? {} : { fetch: options.fetch }),
  });
  const newCorrelationId = options.newCorrelationId ?? (() => crypto.randomUUID());

  client.use({
    onRequest({ request }) {
      const token = options.getToken();
      if (token !== undefined) {
        request.headers.set('Authorization', `Bearer ${token}`);
      }

      request.headers.set('X-Correlation-Id', newCorrelationId());
      return request;
    },
    onResponse({ request, response }) {
      // A sign-in that is refused is a 401 too, but it carried no token, and it is the form's to show.
      if (response.status === 401 && request.headers.has('Authorization')) {
        options.onUnauthorized();
      }

      return undefined;
    },
  });

  return client;
}
