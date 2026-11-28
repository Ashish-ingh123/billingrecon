import { useId } from 'react';
import { Link } from 'react-router';
import { roleLabels, roles, type Role } from '../auth/roles';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import { EmptyState } from '../components/EmptyState';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { SearchInput } from '../components/SearchInput';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { filtersInUse } from '../components/table/listState';
import { useLiveSearch } from '../components/useLiveSearch';
import { formatInstant } from '../format';
import { AdministrationTabs } from './AdministrationTabs';
import { useUserListParams, useUsers, type User, type UserStatus } from './api';

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

const columns: readonly TableColumn<User>[] = [
  {
    id: 'name',
    header: 'Name',
    fixed: true,
    sort: 'displayName',
    cell: (user) => <Link to={`/administration/users/${user.id}`}>{user.displayName}</Link>,
  },
  { id: 'email', header: 'E-mail address', sort: 'email', cell: (user) => user.email },
  { id: 'roles', header: 'Roles', cell: (user) => user.roles.map((role) => roleLabels[role]).join(', ') },
  { id: 'status', header: 'Status', fixed: true, cell: (user) => <UserStatusBadge status={user.status} /> },
  {
    id: 'lastSignIn',
    header: 'Last signed in',
    sort: 'lastSignInAtUtc',
    cell: (user) => (user.lastSignInAtUtc === null ? 'Never' : formatInstant(user.lastSignInAtUtc)),
  },
];

/**
 * The users of the hospital (US-01; ADR 0030, ADR 0051), for its administrator: found by a part of their address while it is typed, by status and by role, a
 * page at a time, each with their roles, their status and when they last signed in, in the order asked for (Prompt 12.2,
 * ADR 0063).
 */
export function UsersPage() {
  const params = useUserListParams();
  const users = useUsers(params);
  const email = useLiveSearch(params.email, (text) => params.change({ email: text }, { replace: true }));
  const emailId = useId();
  const statusId = useId();
  const roleId = useId();
  const filtered = params.inUse > 0;

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
          <SearchInput id={emailId} type="search" busy={users.isFetching} {...email} />
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
        <p className={listStyles.inUse} aria-live="polite">
          <span>{filtersInUse(params.inUse)}</span>
          {filtered && (
            <button type="button" className={buttons.link} onClick={params.clear}>
              Clear filters
            </button>
          )}
        </p>
      </form>
      {users.data === undefined ? (
        <LoadState error={users.error} what="users" />
      ) : users.data.items.length === 0 ? (
        <EmptyState icon="user">{filtered ? 'No user matches.' : 'No user is set up yet.'}</EmptyState>
      ) : (
        <>
          <DataTable
            tableId="users"
            label="Users"
            columns={columns}
            rows={users.data.items}
            keyOf={(user) => user.id}
            sort={params.sort}
            onSort={(sort) => params.change({ sort })}
            busy={users.isFetching}
          />
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
