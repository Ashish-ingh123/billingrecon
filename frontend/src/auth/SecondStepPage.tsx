import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate } from 'react-router';
import { answer, problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { BrandMark } from '../brand/BrandMark';
import { Icon } from '../brand/Icon';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { landingPath } from '../shell/navigation';
import { useAuth } from './AuthContext';
import styles from './ChangePasswordPage.module.css';
import { sessionFromSignIn } from './session';
import { useSignOut } from './useSignOut';

/**
 * The second step of a sign-in (ADR 0061): after the password, a user who has a second step gives the code their
 * authenticator app shows now, or one of their recovery codes. Until then the session reaches this page and nothing
 * else; the answer is a full session.
 */
export function SecondStepPage() {
  const client = useApiClient();
  const navigate = useNavigate();
  const { session, signIn } = useAuth();
  const signOut = useSignOut();
  const [code, setCode] = useState('');
  const [missing, setMissing] = useState(false);

  const giving = useMutation({
    mutationFn: () => answer(client.POST('/api/v1/auth/second-step', { body: { code: code.trim() } })),
    onSuccess: (signedIn) => {
      if (session === null) {
        return;
      }

      const next = sessionFromSignIn(signedIn, session.email, session.hospital);
      if (next !== null) {
        signIn(next);
        void navigate(landingPath(next.roles), { replace: true });
      }
    },
    onError: () => setCode(''),
  });

  // Only a session that waits for its second step has anything to do here.
  if (session !== null && session.secondStep !== 'Pending' && !giving.isSuccess) {
    return <Navigate to="/" replace />;
  }

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setMissing(code.trim() === '');
    if (code.trim() !== '') {
      giving.mutate();
    }
  };

  const problem = giving.error === null ? null : problemOf(giving.error);

  return (
    <main className={styles.page} id="content">
      <section className={styles.card} aria-labelledby="page-title">
        <BrandMark />
        <h1 id="page-title" className={styles.title}>
          Enter your code
        </h1>
        <p className={styles.lead}>
          Your password was right. Now give the six digits your authenticator app shows for BillingRecon.
        </p>
        <form className={forms.form} noValidate onSubmit={submit}>
          {problem !== null && <ProblemAlert problem={problem} />}
          <TextField
            id="second-step-code"
            label="Code"
            requiredMark
            autoComplete="one-time-code"
            autoCapitalize="characters"
            spellCheck={false}
            hint="Lost your phone? Give one of your recovery codes instead; each works once."
            value={code}
            onChange={(event) => setCode(event.target.value)}
            error={missing && code.trim() === '' ? 'Enter the code.' : undefined}
          />
          <div className={forms.actions}>
            <button type="submit" className={buttons.primary} disabled={giving.isPending} aria-busy={giving.isPending}>
              <Icon name="padlock" />
              {giving.isPending ? 'Checking…' : 'Continue'}
            </button>
            <button type="button" className={buttons.link} onClick={signOut}>
              Sign out
            </button>
          </div>
        </form>
      </section>
    </main>
  );
}
