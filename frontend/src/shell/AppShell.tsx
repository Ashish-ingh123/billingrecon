import { useCallback, useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router';
import { useAuth, useSession } from '../auth/AuthContext';
import { roleLabels } from '../auth/roles';
import { BrandMark } from '../brand/BrandMark';
import { CompanyCredit } from '../brand/CompanyCredit';
import { Icon } from '../brand/Icon';
import { formatDate, todayInIndia } from '../format';
import { ThemeToggle } from '../theme/ThemeToggle';
import styles from './AppShell.module.css';
import { sectionsFor } from './navigation';
import { usePageFocus } from './usePageFocus';

export const railStorageKey = 'billingrecon.navigation';

function readRail(): boolean {
  try {
    return window.localStorage.getItem(railStorageKey) === 'rail';
  } catch {
    return false;
  }
}

function storeRail(rail: boolean): void {
  try {
    window.localStorage.setItem(railStorageKey, rail ? 'rail' : 'full');
  } catch {
    // Storage is off: the choice holds until the page is left.
  }
}

/**
 * The frame of every page a signed-in user sees (ADR 0038, ADR 0048): a sidebar with the product, the sections the
 * user's roles may open and the company; a top bar with the hospital, the user, the theme and sign-out; the page.
 * On a wide window the sidebar collapses to a rail of icons, the choice remembered in this browser; under 900 px it is a
 * drawer behind a menu button, which holds the focus while it is open and gives it back when it closes.
 */
export function AppShell() {
  const session = useSession();
  const { signOut } = useAuth();
  const location = useLocation();
  const [rail, setRail] = useState(readRail);
  const [drawer, setDrawer] = useState(false);
  const menuButton = useRef<HTMLButtonElement>(null);
  const sidebar = useRef<HTMLElement>(null);
  usePageFocus();

  const closeDrawer = useCallback((giveFocusBack: boolean) => {
    setDrawer(false);
    if (giveFocusBack) {
      menuButton.current?.focus();
    }
  }, []);

  // An open drawer takes the focus on its first link.
  useEffect(() => {
    if (drawer) {
      sidebar.current?.querySelector<HTMLElement>('a, button')?.focus();
    }
  }, [drawer]);

  const toggleRail = () => {
    setRail((now) => {
      storeRail(!now);
      return !now;
    });
  };

  // While the drawer is open, Escape closes it and Tab goes round inside it.
  useEffect(() => {
    if (!drawer) {
      return;
    }

    const keepFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        closeDrawer(true);
        return;
      }

      if (event.key !== 'Tab' || sidebar.current === null) {
        return;
      }

      // Only what can be seen: the rail's button is hidden in the drawer.
      const stops = [...sidebar.current.querySelectorAll<HTMLElement>('a, button')].filter(
        (stop) => stop.getClientRects().length > 0,
      );
      const first = stops[0];
      const last = stops[stops.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last?.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first?.focus();
      }
    };

    document.addEventListener('keydown', keepFocus);
    return () => document.removeEventListener('keydown', keepFocus);
  }, [drawer, closeDrawer]);

  return (
    <div className={styles.shell} data-rail={rail ? 'true' : undefined} data-drawer={drawer ? 'open' : undefined}>
      <a className={styles.skip} href="#content">
        Skip to content
      </a>

      <aside ref={sidebar} id="sidebar" className={styles.sidebar} aria-label="Sections">
        <div className={styles.product}>
          <BrandMark className={styles.productMark} />
          <span className={styles.productName}>BillingRecon</span>
          <button
            type="button"
            className={styles.drawerClose}
            aria-label="Close the menu"
            onClick={() => closeDrawer(true)}
          >
            <Icon name="close" />
          </button>
        </div>

        <nav className={styles.navigation} aria-label="Main">
          <ul>
            {sectionsFor(session.roles).map((section) => (
              <li key={section.path}>
                <NavLink
                  to={section.path}
                  title={rail ? section.label : undefined}
                  onClick={() => setDrawer(false)}
                  className={({ isActive }) => (isActive ? `${styles.link} ${styles.active}` : styles.link)}
                >
                  <Icon name={section.icon} className={styles.linkIcon} />
                  <span className={styles.linkLabel}>{section.label}</span>
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>

        <NavLink
          to="/change-password"
          className={styles.railToggle}
          title={rail ? 'Change password' : undefined}
          onClick={() => closeDrawer(false)}
        >
          <Icon name="padlock" />
          <span className={styles.linkLabel}>Change password</span>
        </NavLink>

        <button
          type="button"
          className={styles.railToggle}
          aria-pressed={rail}
          aria-label="Collapse the navigation"
          onClick={toggleRail}
        >
          <Icon name={rail ? 'chevrons-right' : 'chevrons-left'} />
          <span className={styles.linkLabel}>Collapse</span>
        </button>

        <footer className={styles.company}>
          <CompanyCredit lead="BillingRecon by" />
        </footer>
      </aside>

      <button
        type="button"
        className={styles.backdrop}
        aria-hidden="true"
        tabIndex={-1}
        onClick={() => closeDrawer(true)}
      />

      <header className={styles.topBar}>
        <button
          ref={menuButton}
          type="button"
          className={styles.menuButton}
          aria-label="Menu"
          aria-expanded={drawer}
          aria-controls="sidebar"
          onClick={() => setDrawer(true)}
        >
          <Icon name="menu" />
        </button>
        <span className={styles.hospital} title="Hospital">
          <Icon name="building" className={styles.hospitalIcon} />
          {session.hospital}
        </span>
        <div className={styles.spacer} />
        <span className={styles.user}>
          <span className={styles.avatar} aria-hidden="true">
            {session.email.slice(0, 1).toUpperCase()}
          </span>
          <span className={styles.userText}>
            <span className={styles.email}>{session.email}</span>
            <span className={styles.roles}>{session.roles.map((role) => roleLabels[role]).join(', ')}</span>
          </span>
        </span>
        <ThemeToggle />
        <button type="button" className={styles.signOut} onClick={() => signOut('signed-out')}>
          <Icon name="enter" className={styles.signOutIcon} />
          Sign out
        </button>
      </header>

      <main id="content" className={styles.content} tabIndex={-1}>
        {/* On paper only: the hospital and the day it was printed, in India (Prompt 9.4b). */}
        <p className={styles.printHead}>
          {session.hospital} · BillingRecon · printed {formatDate(todayInIndia())}
        </p>
        <div key={location.pathname} className={styles.page}>
          <Outlet />
        </div>
      </main>
    </div>
  );
}
