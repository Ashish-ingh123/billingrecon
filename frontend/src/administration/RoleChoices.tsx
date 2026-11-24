import { roleLabels, roles, type Role } from '../auth/roles';
import fieldStyles from '../components/Field.module.css';
import { roleDuties } from './api';
import styles from './Administration.module.css';

/**
 * The roles of a user as five check boxes, each with what the role may do (ADR 0051). At least one is required; the
 * API refuses none, and the form says so before sending.
 */
export function RoleChoices({
  value,
  onChange,
  error,
}: {
  value: readonly Role[];
  onChange: (next: Role[]) => void;
  error?: string | undefined;
}) {
  const toggle = (role: Role, checked: boolean) =>
    onChange(roles.filter((candidate) => (candidate === role ? checked : value.includes(candidate))));

  return (
    <fieldset className={styles.roles} aria-describedby={error === undefined ? undefined : 'roles-error'}>
      <legend className={fieldStyles.label}>Roles</legend>
      {roles.map((role) => (
        <label key={role} className={styles.role}>
          <input
            type="checkbox"
            aria-label={roleLabels[role]}
            aria-describedby={`role-duty-${role}`}
            checked={value.includes(role)}
            onChange={(event) => toggle(role, event.target.checked)}
          />
          <span>
            <strong>{roleLabels[role]}</strong>
            <span id={`role-duty-${role}`} className={styles.duty}>
              {roleDuties[role]}
            </span>
          </span>
        </label>
      ))}
      {error !== undefined && (
        <p id="roles-error" className={fieldStyles.error}>
          {error}
        </p>
      )}
    </fieldset>
  );
}
