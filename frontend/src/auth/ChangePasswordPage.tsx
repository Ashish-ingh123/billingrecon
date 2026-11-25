import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link, useNavigate } from 'react-router';
import { answer, problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import type { ApiProblem } from '../api/problem';
import { BrandMark } from '../brand/BrandMark';
import { Icon } from '../brand/Icon';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { landingPath } from '../shell/navigation';
import { useAuth } from './AuthContext';
import { useSignOut } from './useSignOut';
import styles from './ChangePasswordPage.module.css';
import { sessionFromSignIn } from './session';

interface PasswordFields {
  currentPassword: string;
  newPassword: string;
  repeated: string;
}

const fields = ['currentPassword', 'newPassword'] as const;

/**
 * The signed-in user sets a password of their own (ADR 0051). A user whose password an administrator set comes here
 * first and can go nowhere else until it is done; anyone may come here to change theirs. The answer is a new token, so
 * the user goes on without signing in again.
 */
export function ChangePasswordPage() {
  const client = useApiClient();
  const navigate = useNavigate();
  const { session, signIn } = useAuth();
  const signOut = useSignOut();
  const {
    register,
    handleSubmit,
    setError,
    getValues,
    formState: { errors },
  } = useForm<PasswordFields>({ defaultValues: { currentPassword: '', newPassword: '', repeated: '' } });
  const due = session?.credentialsChangeDue === true;

  const changing = useMutation({
    mutationFn: (values: PasswordFields) =>
      answer(
        client.POST('/api/v1/auth/password', {
          body: { currentPassword: values.currentPassword, newPassword: values.newPassword },
        }),
      ),
    onSuccess: (signedIn) => {
      if (session === null) {
        return;
      }

      const next = sessionFromSignIn(signedIn, session.email, session.hospital);
      if (next !== null) {
        signIn(next);
        void navigate(landingPath(next.roles), { replace: true, state: { passwordChanged: true } });
      }
    },
    onError: (failure) => {
      const problem = problemOf(failure);
      for (const field of fields) {
        const message = problem.fieldErrors[field]?.[0];
        if (message !== undefined) {
          setError(field, { type: 'server', message }, { shouldFocus: true });
        }
      }

      if (problem.errorCode === 'password_current_wrong') {
        setError(
          'currentPassword',
          { type: 'server', message: problem.detail ?? problem.title },
          { shouldFocus: true },
        );
      }
    },
  });

  const problem: ApiProblem | null = changing.error === null ? null : problemOf(changing.error);

  return (
    <main className={styles.page} id="content">
      <section className={styles.card} aria-labelledby="page-title">
        <BrandMark />
        <h1 id="page-title" className={styles.title}>
          {due ? 'Set a password of your own' : 'Change your password'}
        </h1>
        <p className={styles.lead}>
          {due
            ? 'The password you were given was set by an administrator. Choose your own before you go on: 12 characters or more, which nobody else knows.'
            : 'Give your current password, then the new one: 12 characters or more.'}
        </p>
        <form
          className={forms.form}
          noValidate
          onSubmit={(event) => void handleSubmit((values) => changing.mutate(values))(event)}
        >
          {problem !== null && problem.errorCode !== 'password_current_wrong' && (
            <ProblemAlert problem={problem} placedFields={[...fields]} />
          )}
          <TextField
            id="current-password"
            label={due ? 'The password you were given' : 'Current password'}
            type="password"
            autoComplete="current-password"
            requiredMark
            error={errors.currentPassword?.message}
            {...register('currentPassword', { validate: (value) => value !== '' || 'Enter your current password.' })}
          />
          <TextField
            id="new-password"
            label="New password"
            type="password"
            autoComplete="new-password"
            requiredMark
            hint="12 to 128 characters; a few words you will remember are a good choice."
            error={errors.newPassword?.message}
            {...register('newPassword', {
              validate: (value) =>
                value.length < 12
                  ? 'The new password has at least 12 characters.'
                  : value.length > 128
                    ? 'The new password has at most 128 characters.'
                    : value !== getValues('currentPassword') || 'The new password is not the current one.',
            })}
          />
          <TextField
            id="repeated-password"
            label="New password again"
            type="password"
            autoComplete="new-password"
            requiredMark
            error={errors.repeated?.message}
            {...register('repeated', {
              validate: (value) => value === getValues('newPassword') || 'The two new passwords are not the same.',
            })}
          />
          <div className={forms.actions}>
            <button
              type="submit"
              className={buttons.primary}
              disabled={changing.isPending}
              aria-busy={changing.isPending}
            >
              <Icon name="padlock" />
              {changing.isPending ? 'Saving…' : 'Save the new password'}
            </button>
            {due ? (
              <button type="button" className={buttons.link} onClick={signOut}>
                Sign out
              </button>
            ) : (
              <Link to="/">Cancel</Link>
            )}
          </div>
        </form>
      </section>
    </main>
  );
}
