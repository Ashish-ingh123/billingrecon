import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router';
import { accepted, problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import type { ApiProblem } from '../api/problem';
import { BrandMark } from '../brand/BrandMark';
import { Icon } from '../brand/Icon';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import styles from './ChangePasswordPage.module.css';
import { resetLinkFrom } from './resetLink';
import { storeHospital } from './session';

interface ResetFields {
  newPassword: string;
  repeated: string;
}

/**
 * The page the link of a forgotten password opens (ADR 0061, amended). The link carries its token and the hospital's
 * code after the `#`, which no server sees; the page reads them once and takes them out of the address bar, so they
 * stay in no history. The new password is set once; the user then signs in as always, second step included.
 */
export function ResetPasswordPage() {
  const client = useApiClient();
  const [link] = useState(() => {
    const read = resetLinkFrom(window.location.hash);
    if (window.location.hash !== '') {
      window.history.replaceState(window.history.state, '', window.location.pathname);
    }

    return read;
  });
  const {
    register,
    handleSubmit,
    setError,
    getValues,
    formState: { errors },
  } = useForm<ResetFields>({ defaultValues: { newPassword: '', repeated: '' } });

  const setting = useMutation({
    mutationFn: (values: ResetFields) =>
      accepted(
        client.POST('/api/v1/auth/reset-password', {
          body: { tenant: link?.hospital ?? '', token: link?.token ?? '', newPassword: values.newPassword },
        }),
      ),
    onSuccess: () => {
      if (link !== null) {
        storeHospital(link.hospital);
      }
    },
    onError: (failure) => {
      const message = problemOf(failure).fieldErrors.newPassword?.[0];
      if (message !== undefined) {
        setError('newPassword', { type: 'server', message }, { shouldFocus: true });
      }
    },
  });

  const problem: ApiProblem | null = setting.error === null ? null : problemOf(setting.error);
  const spent = link === null || problem?.errorCode === 'password_reset_link_invalid';

  return (
    <main className={styles.page} id="content">
      <section className={styles.card} aria-labelledby="page-title">
        <BrandMark />
        <h1 id="page-title" className={styles.title}>
          Choose a new password
        </h1>
        {setting.isSuccess ? (
          <>
            <p className={styles.lead} role="status">
              Your new password is set, and you are signed out everywhere. Sign in with it now.
            </p>
            <div className={forms.actions}>
              <Link to="/sign-in" className={buttons.primary}>
                <Icon name="enter" />
                Go to the sign-in
              </Link>
            </div>
          </>
        ) : spent ? (
          <>
            <p className={styles.lead} role="alert">
              This link is not good any more: it works once, for 30 minutes, and only the last one sent. Ask for a new
              one.
            </p>
            <div className={forms.actions}>
              <Link to="/forgot-password" className={buttons.primary}>
                <Icon name="envelope" />
                Ask for a new link
              </Link>
              <Link to="/sign-in">Back to the sign-in</Link>
            </div>
          </>
        ) : (
          <>
            <p className={styles.lead}>
              For the hospital <strong>{link.hospital}</strong>. 12 characters or more, which nobody else knows.
            </p>
            <form
              className={forms.form}
              noValidate
              onSubmit={(event) => void handleSubmit((values) => setting.mutate(values))(event)}
            >
              {problem !== null && <ProblemAlert problem={problem} placedFields={['newPassword']} />}
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
                      : value.length <= 128 || 'The new password has at most 128 characters.',
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
                  disabled={setting.isPending}
                  aria-busy={setting.isPending}
                >
                  <Icon name="padlock" />
                  {setting.isPending ? 'Saving…' : 'Save the new password'}
                </button>
                <Link to="/sign-in">Back to the sign-in</Link>
              </div>
            </form>
          </>
        )}
      </section>
    </main>
  );
}
