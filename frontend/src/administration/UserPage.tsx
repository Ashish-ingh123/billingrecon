import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useSession } from '../auth/AuthContext';
import { roleLabels } from '../auth/roles';
import { AuditTrail } from '../components/AuditTrail';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { ConfirmAction } from '../components/ConfirmAction';
import { Details } from '../components/Details';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import summary from '../components/Summary.module.css';
import { formatInstant } from '../format';
import { disableUser, enableUser, resetPassword, resetSecondStep, userKeys, useUser, type User } from './api';
import styles from './Administration.module.css';
import { UserStatusBadge } from './UsersPage';

/** One user (US-01; ADR 0030, ADR 0051): their details, their roles, what their administrator can do, their history. */
export function UserPage() {
  const { id = '' } = useParams();
  const user = useUser(id);

  return user.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="User" back={{ to: '/administration', label: 'Users' }} />
      <LoadState error={user.error} what="the user" />
    </section>
  ) : (
    <UserView user={user.data} />
  );
}

function UserView({ user }: { user: User }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const session = useSession();
  const [done, setDone] = useState<string | null>(null);
  const itself = user.id === session.userId;
  // When the page was opened: a hold that has passed is no longer said.
  const [openedAt] = useState(() => Date.now());

  const changed = (next: User, message: string) => {
    queryClient.setQueryData(userKeys.one(next.id), next);
    void queryClient.invalidateQueries({ queryKey: [...userKeys.all, 'list'] });
    setDone(message);
  };

  const status = useMutation({
    mutationFn: () => (user.status === 'Active' ? disableUser(client, user) : enableUser(client, user)),
    onMutate: () => setDone(null),
    onSuccess: (next) =>
      changed(
        next,
        next.status === 'Disabled'
          ? `${next.displayName} can no longer sign in.`
          : `${next.displayName} can sign in again.`,
      ),
  });

  const secondStep = useMutation({
    mutationFn: () => resetSecondStep(client, user),
    onMutate: () => setDone(null),
    onSuccess: (next) =>
      changed(
        next,
        `${next.displayName} has no second step any more: they set one up at the next sign-in, and approve nothing for 24 hours.`,
      ),
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={user.displayName}
        back={{ to: '/administration', label: 'Users' }}
        subtitle={
          <>
            {user.email} · <UserStatusBadge status={user.status} />
          </>
        }
        actions={
          <>
            <Link className={buttons.secondary} to={`/administration/users/${user.id}/edit`}>
              Edit
            </Link>
            {!itself &&
              (user.status === 'Active' ? (
                <ConfirmAction
                  label="Disable"
                  confirmLabel="Confirm: disable"
                  danger
                  busy={status.isPending}
                  onConfirm={() => status.mutate()}
                />
              ) : (
                <ConfirmAction
                  label="Enable"
                  confirmLabel="Confirm: enable"
                  busy={status.isPending}
                  onConfirm={() => status.mutate()}
                />
              ))}
          </>
        }
      />
      {status.error !== null && <ProblemAlert problem={problemOf(status.error)} />}
      {secondStep.error !== null && <ProblemAlert problem={problemOf(secondStep.error)} />}
      {done !== null && (
        <Banner tone="success" title="Done" live>
          {done}
        </Banner>
      )}
      {user.credentialsChangeDue && (
        <Banner tone="warning" title="Password to be changed">
          {user.displayName} signs in with a password an administrator set and must choose their own before anything
          else.
        </Banner>
      )}
      {user.approvalHoldUntilUtc !== null && Date.parse(user.approvalHoldUntilUtc) > openedAt && (
        <Banner tone="warning" title="Approves nothing for now">
          An administrator reset this user&apos;s password or second step. Until{' '}
          {formatInstant(user.approvalHoldUntilUtc)} {user.displayName} approves and rejects nothing.
        </Banner>
      )}
      {itself && (
        <p className={forms.note}>
          This is you: another administrator disables your user, and you change your own password from the top bar.
        </p>
      )}
      <div className={summary.split}>
        <Details
          items={[
            ['E-mail address', user.email],
            ['Status', user.status],
            ['Last signed in', user.lastSignInAtUtc === null ? 'Never' : formatInstant(user.lastSignInAtUtc)],
            ['Second step', user.secondStepEnrolled ? 'Set up' : 'Not set up'],
            ['Created', formatInstant(user.createdAtUtc)],
            ['Last changed', formatInstant(user.updatedAtUtc)],
          ]}
        />
        <div>
          <div className={styles.roleCard}>
            <h2 className={styles.cardTitle}>Roles</h2>
            <ul className={styles.roleList}>
              {user.roles.map((role) => (
                <li key={role}>{roleLabels[role]}</li>
              ))}
            </ul>
          </div>
        </div>
      </div>
      {!itself && (
        <ResetPassword
          user={user}
          onDone={(next) =>
            changed(next, `A new password is set for ${next.displayName}; they must change it at the next sign-in.`)
          }
        />
      )}
      {!itself && user.secondStepEnrolled && (
        <section className={forms.section} aria-labelledby="second-step-title">
          <h2 id="second-step-title">Second step</h2>
          <p className={forms.note}>
            For a lost phone: take the second step away. {user.displayName} is signed out, sets a new one up at the next
            sign-in, and approves nothing for 24 hours.
          </p>
          <div className={forms.actions}>
            <ConfirmAction
              label="Reset second step"
              confirmLabel="Confirm: reset the second step"
              danger
              busy={secondStep.isPending}
              onConfirm={() => secondStep.mutate()}
            />
          </div>
        </section>
      )}
      <section className={forms.section} aria-labelledby="trail-title">
        <h2 id="trail-title">Audit trail</h2>
        <AuditTrail entityType="User" entityId={user.id} />
      </section>
    </section>
  );
}

/** An administrator sets a new password for a user, who must change it at the next sign-in (ADR 0051). */
function ResetPassword({ user, onDone }: { user: User; onDone: (next: User) => void }) {
  const client = useApiClient();
  const [open, setOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);

  const resetting = useMutation({
    mutationFn: () => resetPassword(client, user, password),
    onSuccess: (next) => {
      setOpen(false);
      setPassword('');
      onDone(next);
    },
  });

  if (!open) {
    return (
      <section className={forms.section} aria-labelledby="reset-title">
        <h2 id="reset-title">Password</h2>
        <p className={forms.note}>
          A user who forgot theirs is given a new one here and chooses their own at the next sign-in.
        </p>
        <button type="button" className={buttons.secondary} onClick={() => setOpen(true)}>
          Reset password
        </button>
      </section>
    );
  }

  return (
    <section className={forms.section} aria-labelledby="reset-title">
      <h2 id="reset-title">Password</h2>
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          if (password.length < 12 || password.length > 128) {
            setError('The password has 12 to 128 characters.');
            return;
          }

          setError(undefined);
          resetting.mutate();
        }}
      >
        {resetting.error !== null && <ProblemAlert problem={problemOf(resetting.error)} placedFields={['password']} />}
        <TextField
          id="reset-password"
          label="New password for the user"
          type="password"
          autoComplete="new-password"
          requiredMark
          hint="Tell it to the user by a safe channel; they must choose their own when they sign in."
          error={error ?? (resetting.error === null ? undefined : problemOf(resetting.error).fieldErrors.password?.[0])}
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={resetting.isPending}
            aria-busy={resetting.isPending}
          >
            {resetting.isPending ? 'Saving…' : 'Set the password'}
          </button>
          <button type="button" className={buttons.link} onClick={() => setOpen(false)}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  );
}
