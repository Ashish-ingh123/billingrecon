import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router';
import { accepted, answer, problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import type { ApiProblem } from '../api/problem';
import { BrandMark } from '../brand/BrandMark';
import { Icon } from '../brand/Icon';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import styles from './ChangePasswordPage.module.css';
import { readHospital } from './session';

interface ForgotFields {
  tenant: string;
  email: string;
}

const fields = ['tenant', 'email'] as const;

/**
 * A forgotten password (ADR 0061, amended): the hospital's code and the address, and a link is sent there if the two
 * belong to a user. The page says the same whatever the API found, as the API does. Where the product has no mail
 * server it says so, and that an administrator sets a new password.
 */
export function ForgotPasswordPage() {
  const client = useApiClient();
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ForgotFields>({ defaultValues: { tenant: readHospital(), email: '' } });

  const options = useQuery({
    queryKey: ['auth', 'options'],
    queryFn: () => answer(client.GET('/api/v1/auth/options')),
    staleTime: Infinity,
    retry: false,
  });

  const asking = useMutation({
    mutationFn: (values: ForgotFields) =>
      accepted(
        client.POST('/api/v1/auth/forgot-password', {
          body: { tenant: values.tenant.trim(), email: values.email.trim() },
        }),
      ),
    onError: (failure) => {
      const refused = problemOf(failure);
      for (const field of fields) {
        const message = refused.fieldErrors[field]?.[0];
        if (message !== undefined) {
          setError(field, { type: 'server', message }, { shouldFocus: true });
        }
      }
    },
  });

  const problem: ApiProblem | null =
    asking.error !== null ? problemOf(asking.error) : options.error !== null ? problemOf(options.error) : null;
  const byMail = options.data?.passwordResetByMail;

  return (
    <main className={styles.page} id="content">
      <section className={styles.card} aria-labelledby="page-title">
        <BrandMark />
        <h1 id="page-title" className={styles.title}>
          Forgot your password?
        </h1>
        {asking.isSuccess ? (
          <>
            <p className={styles.lead} role="status">
              If that hospital code and address belong to a user, a mail with a link is on its way. The link works once,
              for 30 minutes.
            </p>
            <p className={styles.lead}>
              No mail after a few minutes? Check the address and the spam folder, or ask your administrator to set a new
              password for you.
            </p>
            <div className={forms.actions}>
              <Link to="/sign-in">Back to the sign-in</Link>
            </div>
          </>
        ) : byMail === false ? (
          <>
            <p className={styles.lead}>
              This BillingRecon does not send mail, so a password cannot be reset from here. Ask your hospital&rsquo;s
              administrator to set a new password for you: you choose your own at the next sign-in.
            </p>
            <div className={forms.actions}>
              <Link to="/sign-in">Back to the sign-in</Link>
            </div>
          </>
        ) : (
          <>
            <p className={styles.lead}>
              Give your hospital&rsquo;s code and the address you sign in with. We send a link to that address to choose
              a new password.
            </p>
            <form
              className={forms.form}
              noValidate
              onSubmit={(event) => void handleSubmit((values) => asking.mutate(values))(event)}
            >
              {problem !== null && <ProblemAlert problem={problem} placedFields={[...fields]} />}
              <TextField
                id="forgot-tenant"
                label="Hospital code"
                autoComplete="organization"
                requiredMark
                error={errors.tenant?.message}
                {...register('tenant', { validate: (value) => value.trim() !== '' || 'Enter the hospital code.' })}
              />
              <TextField
                id="forgot-email"
                label="E-mail address"
                type="email"
                autoComplete="username"
                requiredMark
                error={errors.email?.message}
                {...register('email', { validate: (value) => value.trim() !== '' || 'Enter your e-mail address.' })}
              />
              <div className={forms.actions}>
                <button
                  type="submit"
                  className={buttons.primary}
                  disabled={asking.isPending || byMail === undefined}
                  aria-busy={asking.isPending}
                >
                  <Icon name="envelope" />
                  {asking.isPending ? 'Sending…' : 'Send the link'}
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
