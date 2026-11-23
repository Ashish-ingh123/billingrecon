/**
 * Where to go after signing in: the page the user was sent away from, when it is a path of this app; otherwise null, for
 * their start page. Anything else (another site, `//host`, `/\host`, the sign-in itself) is ignored, so a link cannot
 * use the sign-in to send a user elsewhere.
 */
export function safeReturnPath(returnTo: string | null): string | null {
  if (returnTo === null || !returnTo.startsWith('/') || returnTo.startsWith('//') || returnTo.startsWith('/\\')) {
    return null;
  }

  return returnTo.startsWith('/sign-in') ? null : returnTo;
}
