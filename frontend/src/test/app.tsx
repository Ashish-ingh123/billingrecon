import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import type { Role } from '../auth/roles';
import { sessionStorageKey, type Session } from '../auth/session';
import { Providers } from '../Providers';
import { routes } from '../routes';

function base64Url(text: string): string {
  const bytes = new TextEncoder().encode(text);
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

/** A JWT with this payload. Its signature means nothing here: the API checks tokens, the browser only reads them. */
export function tokenWithPayload(payload: object): string {
  return `${base64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))}.${base64Url(JSON.stringify(payload))}.signature`;
}

/** A token shaped as the API's (ADR 0031): `sub`, `tenant_id`, and `role`, a string for one role and an array for more. */
export function tokenFor(roles: readonly string[]): string {
  return tokenWithPayload({
    sub: 'd0000000-0000-4000-8000-000000000002',
    tenant_id: 'd0000000-0000-4000-8000-000000000001',
    role: roles.length === 1 ? roles[0] : roles,
    iss: 'BillingRecon',
    aud: 'BillingRecon.Api',
  });
}

/** A session as a sign-in would have kept it, valid for an hour unless said otherwise. */
export function sessionFor(roles: readonly Role[], expiresInMs = 60 * 60 * 1000): Session {
  return {
    accessToken: tokenFor(roles),
    expiresAtUtc: new Date(Date.now() + expiresInMs).toISOString(),
    userId: 'd0000000-0000-4000-8000-000000000002',
    tenantId: 'd0000000-0000-4000-8000-000000000001',
    roles: [...roles],
    email: 'asha@example.org',
    hospital: 'city-hospital',
  };
}

/** An answer of the API, as JSON. */
export function json(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
  });
}

/** A fake of the network: answers each request with the next answer given, and keeps the requests it saw. */
export function fakeApi(...answers: (Response | Error)[]) {
  const requests: Request[] = [];
  const fetch = (request: Request): Promise<Response> => {
    requests.push(request.clone());
    const answer = answers.shift();
    if (answer === undefined) {
      return Promise.reject(new Error(`No answer was given for ${request.method} ${request.url}.`));
    }

    return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer);
  };

  return { fetch, requests };
}

/** Renders the whole app at `path`, with a kept session if one is given, and the network faked by `fetch`. */
export function renderApp(
  path: string,
  options: { session?: Session; fetch?: (request: Request) => Promise<Response> } = {},
) {
  if (options.session !== undefined) {
    window.sessionStorage.setItem(sessionStorageKey, JSON.stringify(options.session));
  }

  const router = createMemoryRouter(routes, { initialEntries: [path] });
  const user = userEvent.setup();
  const view = render(
    <Providers {...(options.fetch === undefined ? {} : { fetch: options.fetch })}>
      <RouterProvider router={router} />
    </Providers>,
  );

  return { router, user, ...view };
}
