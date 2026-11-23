import type { ReactNode } from 'react';
import { Link, Navigate } from 'react-router';
import { useSession } from '../auth/AuthContext';
import { useCan, type Policy } from '../auth/permissions';
import { canOpen, landingPath, type Section } from './navigation';
import styles from './pages.module.css';

/** A section whose screens are not built yet: what it is for, and which prompt builds it. */
export function PlaceholderPage({ section }: { section: Section }) {
  return (
    <section className={styles.page} aria-labelledby="page-title">
      <h1 id="page-title">{section.label}</h1>
      <p>{section.purpose}</p>
      <p className={styles.note}>
        {section.builtBy === 'none yet'
          ? 'No prompt builds these screens yet; the API serves them already.'
          : `These screens arrive with Prompt ${section.builtBy}.`}
      </p>
    </section>
  );
}

/** A section the user's roles may not open, reached by its address. */
export function NoAccessPage() {
  const session = useSession();

  return (
    <section className={styles.page} aria-labelledby="page-title">
      <h1 id="page-title">No access</h1>
      <p>Your roles do not include this page.</p>
      {landingPath(session.roles) !== '/no-access' && <Link to="/">Go to your start page</Link>}
    </section>
  );
}

export function NotFoundPage() {
  return (
    <section className={styles.page} aria-labelledby="page-title">
      <h1 id="page-title">Page not found</h1>
      <p>There is no page at this address.</p>
      <Link to="/">Go to your start page</Link>
    </section>
  );
}

/** A section's page for the roles that may open it, and the no-access page for the others. */
export function SectionPage({ section }: { section: Section }) {
  const session = useSession();

  return canOpen(section, session.roles) ? <PlaceholderPage section={section} /> : <NoAccessPage />;
}

/** A page for the roles of a policy, and the no-access page for the others, reached by its address (ADR 0039 point 3). */
export function RequirePolicy({ policy, children }: { policy: Policy; children: ReactNode }) {
  return useCan(policy) ? children : <NoAccessPage />;
}

/** The start page of the user's roles. */
export function HomeRedirect() {
  const session = useSession();

  return <Navigate to={landingPath(session.roles)} replace />;
}
