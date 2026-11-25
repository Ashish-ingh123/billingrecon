import { useId } from 'react';
import { Link } from 'react-router';
import { roleLabels, roles, type Role } from '../auth/roles';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { EmptyState } from '../components/EmptyState';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { useLiveSearch } from '../components/useLiveSearch';
import { formatInstant } from '../format';
import { AdministrationTabs } from './AdministrationTabs';
import { useUserListParams, useUsers, type UserStatus } from './api';

/** The status of a user as a badge (ADR 0051): active in the colour of success, disabled in the colour of danger. */
export function UserStatusBadge({ status }: { status: UserStatus }) {
  return status === 'Active' ? (
    <Badge tone="success" icon="check-circle">
      Active
    </Badge>
  ) : (
    <Badge tone="danger" icon="ban">
      Disabled
    </Badge>
  );
}

/**
 * The users of the hospital (US-01; ADR 0030, ADR 0051), for its administrator: found by a part of their address while it is typed, by status and by role, a
 * page at a time, each with their roles, their status and when they last signed in.
 */
export function UsersPage() {
  const params = useUserListParams();
  const users = useUsers(params);
  const email = useLiveSearch(params.email, (text) => params.change({ email: text }, { replace: true }));
  const emailId = useId();
  const statusId = useId();
  const roleId = useId();
  const filtered = params.email !== '' || params.status !== undefined || params.role !== undefined;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Users and settings"
        subtitle="Who signs in to this hospital, what each may do, and how the hospital's documents are numbered."
        actions={
          <Link className={buttons.primary} to="/administration/users/new">
            New user
          </Link>
        }
      />
      <AdministrationTabs />
      <form
        className={listStyles.filters}
        role="search"
        aria-label="Find users"
        onSubmit={(event) => event.preventDefault()}
      >
        <div className={listStyles.field}>
          <label htmlFor={emailId}>E-mail address</label>
          <input id={emailId} className={listStyles.input} type="search" autoComplete="off" {...email} />
        </div>
        <div className={listStyles.field}>
          <label htmlFor={statusId}>Status</label>
          <select
            id={statusId}
            className={listStyles.input}
            value={params.status ?? ''}
            onChange={(event) => params.change({ status: (event.target.value || undefined) as UserStatus | undefined })}
          >
            <option value="">All</option>
            <option value="Active">Active</option>
            <option value="Disabled">Disabled</option>
          </select>
        </div>
        <div className={listStyles.field}>
          <label htmlFor={roleId}>Role</label>
          <select
            id={roleId}
            className={listStyles.input}
            value={params.role ?? ''}
            onChange={(event) => params.change({ role: (event.target.value || undefined) as Role | undefined })}
          >
            <option value="">Any role</option>
            {roles.map((role) => (
              <option key={role} value={role}>
                {roleLabels[role]}
              </option>
            ))}
          </select>
        </div>
      </form>
      {users.data === undefined ? (
        <LoadState error={users.error} what="users" />
      ) : users.data.items.length === 0 ? (
        <EmptyState icon="user">{filtered ? 'No user matches.' : 'No user is set up yet.'}</EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">E-mail address</th>
                  <th scope="col">Roles</th>
                  <th scope="col">Status</th>
                  <th scope="col">Last signed in</th>
                </tr>
              </thead>
              <tbody>
                {users.data.items.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <Link to={`/administration/users/${user.id}`}>{user.displayName}</Link>
                    </td>
                    <td>{user.email}</td>
                    <td>{user.roles.map((role) => roleLabels[role]).join(', ')}</td>
                    <td>
                      <UserStatusBadge status={user.status} />
                    </td>
                    <td>{user.lastSignInAtUtc === null ? 'Never' : formatInstant(user.lastSignInAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(users.data.totalCount)}
            onChange={(page) => params.change({ page })}
          />
        </>
      )}
    </section>
  );
}
