import { useMutation } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent } from 'react';
import { Link, Navigate, useNavigate } from 'react-router';
import { answer, problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { BrandMark } from '../brand/BrandMark';
import { Icon } from '../brand/Icon';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { landingPath } from '../shell/navigation';
import { useAuth } from './AuthContext';
import page from './ChangePasswordPage.module.css';
import { qrCode, qrPath } from './qr';
import styles from './SecondStep.module.css';
import { sessionFromSignIn } from './session';
import { useSignOut } from './useSignOut';

/** The key in groups of four, as it is typed into an app. */
const grouped = (secret: string) => secret.replace(/(.{4})(?=.)/g, '$1 ');

/** The QR code of an address, as a picture; nothing when the address is too long for one (the key is typed then). */
function EnrolmentCode({ uri }: { uri: string }) {
  const drawn = useMemo(() => {
    try {
      return qrCode(uri);
    } catch {
      return null;
    }
  }, [uri]);

  if (drawn === null) {
    return null;
  }

  const side = drawn.size + 8;
  return (
    <svg
      className={styles.code}
      viewBox={`0 0 ${String(side)} ${String(side)}`}
      role="img"
      aria-label="QR code for your authenticator app"
      shapeRendering="crispEdges"
    >
      <path d={qrPath(drawn)} />
    </svg>
  );
}

/**
 * The enrolment of a second step (ADR 0061). A user whose role needs one and who has none comes here first and can go
 * nowhere else; anyone else may come here to set one up. Three moments: begin, which makes the secret; the QR code and
 * the key, confirmed by the first code of the app; and the recovery codes, shown this once.
 */
export function EnrolSecondStepPage() {
  const client = useApiClient();
  const navigate = useNavigate();
  const { session, signIn } = useAuth();
  const signOut = useSignOut();
  const [code, setCode] = useState('');
  const [missing, setMissing] = useState(false);
  const forced = session?.secondStep === 'Enrolment';

  const beginning = useMutation({
    mutationFn: () => answer(client.POST('/api/v1/auth/second-step/enrolment', {})),
  });

  const confirming = useMutation({
    mutationFn: () =>
      answer(client.POST('/api/v1/auth/second-step/enrolment/confirm', { body: { code: code.trim() } })),
    onSuccess: (enrolled) => {
      // Every earlier session ended with the enrolment: the answer's is the one to go on with.
      if (session !== null) {
        const next = sessionFromSignIn(enrolled.signedIn, session.email, session.hospital);
        if (next !== null) {
          signIn(next);
        }
      }
    },
    onError: () => setCode(''),
  });

  // A session that waits for the code of the second step it has cannot enrol another.
  if (session?.secondStep === 'Pending') {
    return <Navigate to="/second-step" replace />;
  }

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setMissing(code.trim() === '');
    if (code.trim() !== '') {
      confirming.mutate();
    }
  };

  const begun = beginning.data;
  const enrolled = confirming.data;
  const problem =
    confirming.error !== null
      ? problemOf(confirming.error)
      : beginning.error !== null
        ? problemOf(beginning.error)
        : null;

  return (
    <main className={page.page} id="content">
      <section className={page.card} aria-labelledby="page-title">
        <BrandMark />
        <h1 id="page-title" className={page.title}>
          {enrolled !== undefined ? 'Keep your recovery codes' : 'Set up your second step'}
        </h1>

        {enrolled !== undefined ? (
          <>
            <Banner tone="success" title="Your second step is set up" live>
              From now on you sign in with your password and a code of your app.
            </Banner>
            <p className={page.lead}>
              These ten codes are shown this once. Each signs you in once without your phone. Write them down or print
              them, and keep them where only you find them.
            </p>
            <ul className={styles.recovery} aria-label="Recovery codes">
              {enrolled.recoveryCodes.map((recovery) => (
                <li key={recovery}>{recovery}</li>
              ))}
            </ul>
            <div className={forms.actions}>
              <button
                type="button"
                className={buttons.primary}
                onClick={() => void navigate(landingPath(session?.roles ?? []), { replace: true })}
              >
                I have kept them: continue
              </button>
            </div>
          </>
        ) : begun === undefined ? (
          <>
            <p className={page.lead}>
              {forced
                ? 'Your role approves and administers, so your sign-in needs a second step: a code from an authenticator app on your phone, after your password.'
                : 'A second step protects your sign-in: after your password, a code from an authenticator app on your phone.'}
            </p>
            <ol className={styles.steps}>
              <li>Install an authenticator app on your phone, if you have none.</li>
              <li>Begin here: a QR code is shown for the app to scan.</li>
              <li>Give the six digits the app then shows, and keep the recovery codes you are given.</li>
            </ol>
            {problem !== null && <ProblemAlert problem={problem} />}
            <div className={forms.actions}>
              <button
                type="button"
                className={buttons.primary}
                disabled={beginning.isPending}
                aria-busy={beginning.isPending}
                onClick={() => beginning.mutate()}
              >
                <Icon name="padlock" />
                {beginning.isPending ? 'Preparing…' : 'Begin'}
              </button>
              {forced ? (
                <button type="button" className={buttons.link} onClick={signOut}>
                  Sign out
                </button>
              ) : (
                <Link to="/">Cancel</Link>
              )}
            </div>
          </>
        ) : (
          <form className={forms.form} noValidate onSubmit={submit}>
            <p className={page.lead}>
              Scan this code with your authenticator app, or type the key into it. Then give the six digits it shows.
            </p>
            <EnrolmentCode uri={begun.provisioningUri} />
            <p id="second-step-key-label">The key, if you cannot scan:</p>
            <code className={styles.key} aria-labelledby="second-step-key-label">
              {grouped(begun.secret)}
            </code>
            {problem !== null && <ProblemAlert problem={problem} />}
            <TextField
              id="enrol-code"
              label="Code of the app"
              requiredMark
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(event) => setCode(event.target.value)}
              error={missing && code.trim() === '' ? 'Enter the six digits your app shows.' : undefined}
            />
            <div className={forms.actions}>
              <button
                type="submit"
                className={buttons.primary}
                disabled={confirming.isPending}
                aria-busy={confirming.isPending}
              >
                {confirming.isPending ? 'Checking…' : 'Confirm'}
              </button>
              {forced ? (
                <button type="button" className={buttons.link} onClick={signOut}>
                  Sign out
                </button>
              ) : (
                <Link to="/">Cancel</Link>
              )}
            </div>
          </form>
        )}
      </section>
    </main>
  );
}
