import { NavLink } from 'react-router';
import { Icon } from '../brand/Icon';
import styles from './Administration.module.css';

/** The two parts of "Users and settings" (ADR 0051): a pair of tabs under the page's title. */
export function AdministrationTabs() {
  return (
    <nav className={styles.tabs} aria-label="Users and settings">
      <NavLink end to="/administration" className={({ isActive }) => (isActive ? styles.active : undefined)}>
        <Icon name="user" />
        Users
      </NavLink>
      <NavLink to="/administration/settings" className={({ isActive }) => (isActive ? styles.active : undefined)}>
        <Icon name="gear" />
        Settings
      </NavLink>
    </nav>
  );
}
