/**
 * What the build was given (ADR 0047). The frontend is static files, so its configuration is fixed when it is built:
 * Vite reads the variables named VITE_* from the environment (the frontend image takes them as build arguments from
 * .env, docs/runbook.md section 12). No request is made for any of it.
 */

const address = /^[^\s@<>"]+@[^\s@<>"]+\.[^\s@<>"]+$/;

/**
 * The address "Contact support" writes to on the sign-in page, VITE_SUPPORT_EMAIL, or null when none is set or what is
 * set is not an e-mail address: the link is then left out.
 */
export function supportEmail(value: unknown = import.meta.env.VITE_SUPPORT_EMAIL): string | null {
  if (typeof value !== 'string') {
    return null;
  }

  const trimmed = value.trim();
  return address.test(trimmed) ? trimmed : null;
}
