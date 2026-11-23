import { describe, expect, it, vi } from 'vitest';
import { fakeApi, json } from '../test/app';
import { createApiClient } from './client';
import { problemFrom, unreachableProblem } from './problem';

const origin = 'http://localhost:3000';

describe('the API client (ADR 0038, point 4)', () => {
  it("sends the user's token and a correlation id of its own with every request", async () => {
    const api = fakeApi(json(200, { name: 'BillingRecon' }), json(200, { name: 'BillingRecon' }));
    let id = 0;
    const client = createApiClient({
      baseUrl: origin,
      getToken: () => 'the-token',
      onUnauthorized: vi.fn(),
      fetch: api.fetch,
      newCorrelationId: () => `correlation-${++id}`,
    });

    await client.GET('/api/v1/system/info');
    await client.GET('/api/v1/system/info');

    expect(api.requests.map((request) => request.headers.get('Authorization'))).toEqual([
      'Bearer the-token',
      'Bearer the-token',
    ]);
    expect(api.requests.map((request) => request.headers.get('X-Correlation-Id'))).toEqual([
      'correlation-1',
      'correlation-2',
    ]);
    expect(api.requests[0]?.url).toBe(`${origin}/api/v1/system/info`);
  });

  it('sends no Authorization header without a token', async () => {
    const api = fakeApi(json(200, {}));
    const client = createApiClient({
      baseUrl: origin,
      getToken: () => undefined,
      onUnauthorized: vi.fn(),
      fetch: api.fetch,
    });

    await client.GET('/api/v1/system/info');

    expect(api.requests[0]?.headers.has('Authorization')).toBe(false);
  });

  it('ends the session when the API refuses the token a request carried', async () => {
    const onUnauthorized = vi.fn();
    const api = fakeApi(json(401, { title: 'Unauthorized', status: 401, errorCode: 'unauthorized' }));
    const client = createApiClient({ baseUrl: origin, getToken: () => 'expired', onUnauthorized, fetch: api.fetch });

    const { error, response } = await client.GET('/api/v1/system/info');

    expect(onUnauthorized).toHaveBeenCalledOnce();
    expect(problemFrom(response, error).errorCode).toBe('unauthorized');
  });

  it('leaves a refused sign-in, which carried no token, to the form', async () => {
    const onUnauthorized = vi.fn();
    const api = fakeApi(json(401, { title: 'Unauthorized', status: 401, errorCode: 'invalid_credentials' }));
    const client = createApiClient({ baseUrl: origin, getToken: () => undefined, onUnauthorized, fetch: api.fetch });

    await client.POST('/api/v1/auth/login', { body: { tenant: 'h', email: 'a@b.c', password: 'p' } });

    expect(onUnauthorized).not.toHaveBeenCalled();
  });
});

describe('what a refusal says (ADR 0011)', () => {
  it('reads a ProblemDetails: its code, message, fields, reference and wait', () => {
    const response = new Response(null, { status: 400, headers: { 'Retry-After': '60' } });
    const problem = problemFrom(response, {
      title: 'One or more fields are invalid.',
      status: 400,
      detail: 'Check the fields.',
      errorCode: 'validation_failed',
      errors: { email: ['Enter an address.', 42], password: [] },
      correlationId: 'abc',
    });

    expect(problem).toEqual({
      status: 400,
      errorCode: 'validation_failed',
      title: 'One or more fields are invalid.',
      detail: 'Check the fields.',
      fieldErrors: { email: ['Enter an address.'] },
      correlationId: 'abc',
      retryAfterSeconds: 60,
    });
  });

  it('takes the reference from the header when the body has none, and describes an answer that is no ProblemDetails', () => {
    const response = new Response(null, { status: 502, headers: { 'X-Correlation-Id': 'from-header' } });
    const problem = problemFrom(response, '<html>Bad gateway</html>');

    expect(problem.errorCode).toBeNull();
    expect(problem.title).toBe('Something went wrong on the server. Try again in a moment.');
    expect(problem.correlationId).toBe('from-header');
    expect(problem.retryAfterSeconds).toBeNull();
  });

  it('describes a request that got no answer', () => {
    expect(unreachableProblem()).toMatchObject({ status: 0, errorCode: null, correlationId: null });
  });
});
