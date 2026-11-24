import type { ReactNode } from 'react';
import { Link, Navigate } from 'react-router';
import { useSession } from '../auth/AuthContext';
import { Icon, type IconName } from '../brand/Icon';
import buttons from '../components/buttons.module.css';
import { useCan, type Policy } from '../auth/permissions';
import { canOpen, landingPath, type Section } from './navigation';
import styles from './pages.module.css';

/** A page that says where the user is not, and the way back (ADR 0049): a drawing of our own, the words, the link. */
function Notice({ icon, children }: { icon: IconName; children: ReactNode }) {
  return (
    <section className={styles.page} aria-labelledby="page-title">
      <span className={styles.drawing} aria-hidden="true">
        <Icon name={icon} />
      </span>
      {children}
    </section>
  );
}

/** A section whose screens are not built yet: what it is for, and which prompt builds it. */
export function PlaceholderPage({ section }: { section: Section }) {
  return (
    <Notice icon={section.icon}>
      <h1 id="page-title">{section.label}</h1>
      <p>{section.purpose}</p>
      <p className={styles.note}>
        {section.builtBy === 'none yet'
          ? 'No prompt builds these screens yet; the API serves them already.'
          : `These screens arrive with Prompt ${section.builtBy}.`}
      </p>
    </Notice>
  );
}

/** A section the user's roles may not open, reached by its address. */
export function NoAccessPage() {
  const session = useSession();

  return (
    <Notice icon="padlock">
      <h1 id="page-title">No access</h1>
      <p>Your roles do not include this page.</p>
      {landingPath(session.roles) !== '/no-access' && (
        <Link className={buttons.secondary} to="/">
          Go to your start page
        </Link>
      )}
    </Notice>
  );
}

export function NotFoundPage() {
  return (
    <Notice icon="help">
      <h1 id="page-title">Page not found</h1>
      <p>There is no page at this address.</p>
      <Link className={buttons.secondary} to="/">
        Go to your start page
      </Link>
    </Notice>
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
