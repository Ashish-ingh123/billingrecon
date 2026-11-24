import { Navigate, Outlet, useLocation } from 'react-router';
import { useAuth } from './AuthContext';

/**
 * Lets a signed-in user through. Anyone else goes to the sign-in: after a sign-out, plainly; otherwise (a first visit, an
 * expired or refused token) with the page they were on, to bring them back to it.
 */
export function RequireAuth() {
  const { session, ended } = useAuth();
  const location = useLocation();

  if (session === null) {
    if (ended === 'signed-out') {
      return <Navigate to="/sign-in" replace />;
    }

    const returnTo = encodeURIComponent(location.pathname + location.search);
    return <Navigate to={`/sign-in?returnTo=${returnTo}`} replace />;
  }

  // A password set by an administrator is changed before anything else (ADR 0051).
  if (session.credentialsChangeDue && location.pathname !== '/change-password') {
    return <Navigate to="/change-password" replace />;
  }

  return <Outlet />;
}
