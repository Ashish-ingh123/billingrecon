import { NavLink } from 'react-router';
import { Icon } from '../brand/Icon';
import styles from './Administration.module.css';

/**
 * The parts of "Users and settings" (ADR 0051): tabs under the page's title. Since Prompt 10.3b a third, Import, for
 * bringing in a hospital's records and opening balances (ADR 0053).
 */
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
      <NavLink to="/administration/import" className={({ isActive }) => (isActive ? styles.active : undefined)}>
        <Icon name="building" />
        Import
      </NavLink>
    </nav>
  );
}
