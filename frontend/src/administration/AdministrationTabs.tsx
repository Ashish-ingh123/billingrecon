import { NavLink } from 'react-router';
import { Icon } from '../brand/Icon';
import styles from './Administration.module.css';

/**
 * The parts of "Users and settings" (ADR 0051): tabs under the page's title. Since Prompt 10.3b a third, Import, for
 * bringing in a hospital's records and opening balances (ADR 0053). Since Prompt 10.4c a fourth, Print, for how the
 * hospital's documents are printed (ADR 0059). Since Prompt 11.4b a fifth, Accounts, for the names of the accountant's
 * ledgers (ADR 0062). Since Prompt 13.1c a sixth, Channels, for the bank and gateway accounts whose statements are
 * reconciled (ADR 0065).
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
      <NavLink to="/administration/print" className={({ isActive }) => (isActive ? styles.active : undefined)}>
        <Icon name="receipt" />
        Print
      </NavLink>
      <NavLink to="/administration/import" className={({ isActive }) => (isActive ? styles.active : undefined)}>
        <Icon name="building" />
        Import
      </NavLink>
      <NavLink to="/administration/channels" className={({ isActive }) => (isActive ? styles.active : undefined)}>
        <Icon name="handshake" />
        Channels
      </NavLink>
      <NavLink to="/administration/accounting" className={({ isActive }) => (isActive ? styles.active : undefined)}>
        <Icon name="report" />
        Accounts
      </NavLink>
    </nav>
  );
}
