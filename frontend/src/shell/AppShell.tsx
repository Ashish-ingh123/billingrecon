import { NavLink, Outlet } from 'react-router';
import { useAuth, useSession } from '../auth/AuthContext';
import { roleLabels } from '../auth/roles';
import { ThemeSelect } from '../theme/ThemeSelect';
import styles from './AppShell.module.css';
import { sectionsFor } from './navigation';

/**
 * The frame of every page a signed-in user sees: the top bar with the hospital, the user, the theme and sign-out, and
 * the navigation of the sections the user's roles may open (ADR 0038).
 */
export function AppShell() {
  const session = useSession();
  const { signOut } = useAuth();

  return (
    <div className={styles.shell}>
      <a className={styles.skip} href="#content">
        Skip to content
      </a>
      <header className={styles.topBar}>
        <span className={styles.product}>BillingRecon</span>
        <span className={styles.hospital} title="Hospital">
          {session.hospital}
        </span>
        <div className={styles.spacer} />
        <span className={styles.user}>
          <span>{session.email}</span>
          <span className={styles.roles}>{session.roles.map((role) => roleLabels[role]).join(', ')}</span>
        </span>
        <ThemeSelect />
        <button type="button" className={styles.signOut} onClick={() => signOut('signed-out')}>
          Sign out
        </button>
      </header>
      <nav className={styles.navigation} aria-label="Main">
        <ul>
          {sectionsFor(session.roles).map((section) => (
            <li key={section.path}>
              <NavLink to={section.path} className={({ isActive }) => (isActive ? styles.active : undefined)}>
                {section.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
      <main id="content" className={styles.content} tabIndex={-1}>
        <Outlet />
      </main>
    </div>
  );
}
