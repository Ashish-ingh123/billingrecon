import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Navigate, useNavigate, useSearchParams } from 'react-router';
import { useMutation } from '@tanstack/react-query';
import { useApiClient } from '../api/ApiClientContext';
import { problemFrom, unreachableProblem, type ApiProblem } from '../api/problem';
import { ProblemAlert } from '../components/ProblemAlert';
import { landingPath } from '../shell/navigation';
import { ThemeSelect } from '../theme/ThemeSelect';
import { useAuth } from './AuthContext';
import { safeReturnPath } from './returnPath';
import { readHospital, sessionFromSignIn, storeHospital, type Session } from './session';
import styles from './SignInPage.module.css';

interface SignInFields {
  tenant: string;
  email: string;
  password: string;
}

const fields: readonly (keyof SignInFields)[] = ['tenant', 'email', 'password'];

class SignInRefused extends Error {
  readonly problem: ApiProblem;

  constructor(problem: ApiProblem) {
    super(problem.title);
    this.problem = problem;
  }
}

/** The sign-in (US-02, ADR 0031): the hospital's code, the e-mail address and the password. */
export function SignInPage() {
  const client = useApiClient();
  const { session, ended, signIn } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const returnTo = safeReturnPath(searchParams.get('returnTo'));
  const [problem, setProblem] = useState<ApiProblem | null>(null);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<SignInFields>({ defaultValues: { tenant: readHospital(), email: '', password: '' } });

  const signingIn = useMutation({
    mutationFn: async (values: SignInFields): Promise<Session> => {
      let answer;
      try {
        answer = await client.POST('/api/v1/auth/login', { body: values });
      } catch {
        throw new SignInRefused(unreachableProblem());
      }

      const { data, error, response } = answer;
      if (data === undefined) {
        throw new SignInRefused(problemFrom(response, error));
      }

      const signedIn = sessionFromSignIn(data, values.email, values.tenant);
      if (signedIn === null) {
        throw new SignInRefused(problemFrom(new Response(null, { status: 500 }), null));
      }

      return signedIn;
    },
    onSuccess: (signedIn, values) => {
      storeHospital(values.tenant);
      signIn(signedIn);
      void navigate(returnTo ?? landingPath(signedIn.roles), { replace: true });
    },
    onError: (failure) => {
      const refused = failure instanceof SignInRefused ? failure.problem : unreachableProblem();
      for (const field of fields) {
        const message = refused.fieldErrors[field]?.[0];
        if (message !== undefined) {
          setError(field, { type: 'server', message });
        }
      }

      setProblem(refused);
    },
  });

  if (session !== null && !signingIn.isPending) {
    return <Navigate to={returnTo ?? landingPath(session.roles)} replace />;
  }

  const submit = handleSubmit((values) => {
    setProblem(null);
    signingIn.mutate(values);
  });

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <ThemeSelect />
      </header>
      <main className={styles.main}>
        <form
          className={styles.card}
          onSubmit={(event) => void submit(event)}
          noValidate
          aria-labelledby="sign-in-title"
        >
          <h1 id="sign-in-title" className={styles.title}>
            Sign in to BillingRecon
          </h1>
          {/* How the last session ended, until a new attempt has something of its own to say. */}
          {problem === null && ended === 'expired' && (
            <p className={styles.notice}>Your session has ended. Sign in again to continue.</p>
          )}
          {problem === null && ended === 'signed-out' && <p className={styles.notice}>You have signed out.</p>}
          {problem !== null && <ProblemAlert problem={problem} placedFields={fields} />}

          <label className={styles.label} htmlFor="sign-in-tenant">
            Hospital code
          </label>
          <input
            id="sign-in-tenant"
            className={styles.input}
            autoComplete="organization"
            aria-invalid={errors.tenant !== undefined}
            aria-describedby={errors.tenant ? 'sign-in-tenant-error' : undefined}
            {...register('tenant', { validate: (value) => value.trim() !== '' || 'Enter the hospital code.' })}
          />
          {errors.tenant && (
            <p id="sign-in-tenant-error" className={styles.fieldError}>
              {errors.tenant.message}
            </p>
          )}

          <label className={styles.label} htmlFor="sign-in-email">
            E-mail address
          </label>
          <input
            id="sign-in-email"
            className={styles.input}
            type="email"
            autoComplete="username"
            aria-invalid={errors.email !== undefined}
            aria-describedby={errors.email ? 'sign-in-email-error' : undefined}
            {...register('email', { validate: (value) => value.trim() !== '' || 'Enter your e-mail address.' })}
          />
          {errors.email && (
            <p id="sign-in-email-error" className={styles.fieldError}>
              {errors.email.message}
            </p>
          )}

          <label className={styles.label} htmlFor="sign-in-password">
            Password
          </label>
          <input
            id="sign-in-password"
            className={styles.input}
            type="password"
            autoComplete="current-password"
            aria-invalid={errors.password !== undefined}
            aria-describedby={errors.password ? 'sign-in-password-error' : undefined}
            {...register('password', { validate: (value) => value !== '' || 'Enter your password.' })}
          />
          {errors.password && (
            <p id="sign-in-password-error" className={styles.fieldError}>
              {errors.password.message}
            </p>
          )}

          <button type="submit" className={styles.submit} disabled={signingIn.isPending}>
            {signingIn.isPending ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </main>
    </div>
  );
}
