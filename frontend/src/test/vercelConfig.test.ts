import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The free hosting (Vercel for the app, the API elsewhere) serves the built app with frontend/vercel.json instead of
 * nginx. These tests hold the two to the same answers: the headers nginx adds to the app's pages and to /assets/ (the
 * content security policy with the theme script's SHA-256, no framing, no sniffing, HTTPS for a year, the caching), and
 * the routing (/api and /health passed to the API, every other path that is not a file answered with index.html, a
 * missing file under /assets/ a 404). A change to one file that is not made to the other fails here.
 */
interface Header {
  key: string;
  value: string;
}

interface VercelConfig {
  rewrites: { source: string; destination: string }[];
  headers: { source: string; headers: Header[] }[];
}

const vercel = JSON.parse(readFileSync(join(process.cwd(), 'vercel.json'), 'utf8')) as VercelConfig;
const nginx = readFileSync(join(process.cwd(), 'deploy', 'nginx.conf'), 'utf8');

/** The value of nginx's `set $csp "..."`. */
const nginxCsp = /set \$csp "([^"]*)";/.exec(nginx)?.[1];

/** The headers nginx adds in `location <path> { ... }`, with $csp read as its value. */
function nginxHeaders(path: string): Record<string, string> {
  const escaped = path.replace(/[/.]/g, '\\$&');
  const body =
    new RegExp(`location ${escaped} \\{([^}]*)\\}`).exec(nginx.slice(nginx.indexOf('listen 443')))?.[1] ?? '';
  const headers: Record<string, string> = {};
  for (const [, key = '', raw = ''] of body.matchAll(/add_header (\S+) (.+?) always;/g)) {
    const value = raw.startsWith('"') ? raw.slice(1, -1) : raw;
    headers[key] = value === '$csp' ? (nginxCsp ?? '') : value;
  }

  return headers;
}

/** A source of vercel.json as the regular expression Vercel reads it as (each one here is a plain pattern). */
function matches(source: string, path: string): boolean {
  return new RegExp(`^${source}$`).test(path);
}

/** The headers vercel.json adds to `path`: every matching entry, a later one overriding an earlier one's key. */
function vercelHeaders(path: string): Record<string, string> {
  const headers: Record<string, string> = {};
  for (const entry of vercel.headers.filter((candidate) => matches(candidate.source, path))) {
    for (const header of entry.headers) {
      headers[header.key] = header.value;
    }
  }

  return headers;
}

describe('vercel.json', () => {
  it('lists the content security policy nginx sends', () => {
    expect(nginxCsp).toBeDefined();
    expect(vercelHeaders('/')['Content-Security-Policy']).toBe(nginxCsp);
  });

  it.each(['/', '/sign-in', '/bills/12', '/index.html'])('adds the headers of nginx\'s "location /" to %s', (path) => {
    const expected = nginxHeaders('/');

    expect(Object.keys(expected)).toHaveLength(6);
    expect(vercelHeaders(path)).toEqual(expected);
  });

  it('adds the headers of nginx\'s "location /assets/" to the built files', () => {
    const expected = nginxHeaders('/assets/');

    expect(Object.keys(expected)).toHaveLength(4);
    expect(vercelHeaders('/assets/index-Bx12.js')).toEqual(expected);
  });

  it.each(['/api/v1/auth/login', '/health/ready'])('leaves the answers of the API to the API (%s)', (path) => {
    expect(vercelHeaders(path)).toEqual({});
  });

  it('passes /api and /health to one API over HTTPS, path and all', () => {
    const api = vercel.rewrites.find((rewrite) => rewrite.source === '/api/:path*');
    const health = vercel.rewrites.find((rewrite) => rewrite.source === '/health/:path*');

    expect(api?.destination).toMatch(/^https:\/\/[^/]+\/api\/:path\*$/);
    expect(health?.destination).toMatch(/^https:\/\/[^/]+\/health\/:path\*$/);
    expect(new URL(api?.destination ?? '').origin).toBe(new URL(health?.destination ?? '').origin);
  });

  it("answers index.html for the app's own paths only", () => {
    const fallback = vercel.rewrites.find((rewrite) => rewrite.destination === '/index.html');

    expect(fallback).toBeDefined();
    for (const path of ['/', '/sign-in', '/bills/12', '/reports/aging']) {
      expect(matches(fallback?.source ?? '', path)).toBe(true);
    }
    for (const path of ['/api/v1/bills', '/health/ready', '/assets/missing.js']) {
      expect(matches(fallback?.source ?? '', path)).toBe(false);
    }
  });
});
