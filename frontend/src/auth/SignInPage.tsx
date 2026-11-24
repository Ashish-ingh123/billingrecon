import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Navigate, useNavigate, useSearchParams } from 'react-router';
import { useMutation } from '@tanstack/react-query';
import { useApiClient } from '../api/ApiClientContext';
import { problemFrom, unreachableProblem, type ApiProblem } from '../api/problem';
import { BrandMark } from '../brand/BrandMark';
import { CompanyCredit } from '../brand/CompanyCredit';
import { Icon, type IconName } from '../brand/Icon';
import { ProblemAlert } from '../components/ProblemAlert';
import { supportEmail } from '../config';
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

const productName = 'BillingRecon';

const fields: readonly (keyof SignInFields)[] = ['tenant', 'email', 'password'];

class SignInRefused extends Error {
  readonly problem: ApiProblem;

  constructor(problem: ApiProblem) {
    super(problem.title);
    this.problem = problem;
  }
}

/**
 * The sign-in (US-02, ADR 0031): the hospital's code, the e-mail address and the password, laid out as
 * docs/ui/sign-in.png (ADR 0047): the brand on the blue at the left, the card at the right, the company at the bottom.
 */
export function SignInPage() {
  const client = useApiClient();
  const { session, ended, signIn } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const returnTo = safeReturnPath(searchParams.get('returnTo'));
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [shown, setShown] = useState(false);
  const support = supportEmail();

  // The page names itself in the tab while it is shown.
  useEffect(() => {
    document.title = `Sign in · ${productName}`;
    return () => {
      document.title = productName;
    };
  }, []);

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
      <SignInBackdrop />
      <header className={styles.theme}>
        <ThemeSelect />
      </header>

      <section className={styles.brand} aria-label="BillingRecon">
        <div className={styles.brandRow}>
          <BrandMark className={styles.brandMark} />
          <div>
            <p className={styles.brandName}>BillingRecon</p>
            <p className={styles.tagline}>Reconcile. Verify. Resolve.</p>
          </div>
        </div>
        <ul className={styles.promises}>
          {promises.map(([icon, text]) => (
            <li key={text} className={styles.promise}>
              <Icon name={icon} className={styles.promiseIcon} />
              <span>{text}</span>
            </li>
          ))}
        </ul>
      </section>

      <main className={styles.main}>
        <form
          className={styles.card}
          onSubmit={(event) => void submit(event)}
          noValidate
          aria-labelledby="sign-in-title"
        >
          <PulseLine />
          <h1 id="sign-in-title" className={styles.title}>
            Sign in to BillingRecon
          </h1>
          <p className={styles.subtitle}>Access your hospital&rsquo;s billing dashboard</p>
          {/* How the last session ended, until a new attempt has something of its own to say. */}
          {problem === null && ended === 'expired' && (
            <p className={styles.notice}>Your session has ended. Sign in again to continue.</p>
          )}
          {problem === null && ended === 'signed-out' && <p className={styles.notice}>You have signed out.</p>}
          {problem !== null && (
            <div className={styles.refusal}>
              <ProblemAlert problem={problem} placedFields={fields} />
            </div>
          )}

          <label className={styles.label} htmlFor="sign-in-tenant">
            <Icon name="building" className={styles.labelIcon} />
            Hospital code
          </label>
          <div className={styles.control}>
            <input
              id="sign-in-tenant"
              className={styles.input}
              autoComplete="organization"
              aria-invalid={errors.tenant !== undefined}
              aria-describedby={errors.tenant ? 'sign-in-tenant-error' : undefined}
              {...register('tenant', { validate: (value) => value.trim() !== '' || 'Enter the hospital code.' })}
            />
          </div>
          {errors.tenant && (
            <p id="sign-in-tenant-error" className={styles.fieldError}>
              {errors.tenant.message}
            </p>
          )}

          <label className={styles.label} htmlFor="sign-in-email">
            <Icon name="envelope" className={styles.labelIcon} />
            E-mail address
          </label>
          <div className={styles.control}>
            <input
              id="sign-in-email"
              className={styles.input}
              type="email"
              autoComplete="username"
              placeholder="you@hospital.org"
              aria-invalid={errors.email !== undefined}
              aria-describedby={errors.email ? 'sign-in-email-error' : undefined}
              {...register('email', { validate: (value) => value.trim() !== '' || 'Enter your e-mail address.' })}
            />
            <Icon name="envelope-outline" className={styles.adornment} />
          </div>
          {errors.email && (
            <p id="sign-in-email-error" className={styles.fieldError}>
              {errors.email.message}
            </p>
          )}

          <label className={styles.label} htmlFor="sign-in-password">
            <Icon name="padlock" className={styles.labelIcon} />
            Password
          </label>
          <div className={styles.control}>
            <input
              id="sign-in-password"
              className={styles.input}
              type={shown ? 'text' : 'password'}
              autoComplete="current-password"
              placeholder="Enter your password"
              aria-invalid={errors.password !== undefined}
              aria-describedby={errors.password ? 'sign-in-password-error' : undefined}
              {...register('password', { validate: (value) => value !== '' || 'Enter your password.' })}
            />
            {/* One name and aria-pressed: pressed means the password is shown. */}
            <button
              type="button"
              className={styles.reveal}
              aria-label="Show password"
              aria-pressed={shown}
              onClick={() => setShown((now) => !now)}
            >
              <span key={String(shown)} className={styles.swap}>
                <Icon name={shown ? 'eye' : 'eye-off'} />
              </span>
            </button>
          </div>
          {errors.password && (
            <p id="sign-in-password-error" className={styles.fieldError}>
              {errors.password.message}
            </p>
          )}

          <button type="submit" className={styles.submit} disabled={signingIn.isPending}>
            <Icon
              name={signingIn.isPending ? 'spinner' : 'enter'}
              className={signingIn.isPending ? styles.spinner : undefined}
            />
            {signingIn.isPending ? 'Signing in…' : 'Sign in'}
          </button>

          {support !== null && (
            <div className={styles.help}>
              <p className={styles.divider}>
                <span>Need help?</span>
              </p>
              <a className={styles.support} href={`mailto:${support}`}>
                <Icon name="help" />
                Contact support
              </a>
            </div>
          )}
        </form>
      </main>

      <footer className={styles.company}>
        <CompanyCredit lead="A product of" />
      </footer>
    </div>
  );
}

const promises: readonly (readonly [IconName, string])[] = [
  ['shield-check', 'Secure hospital billing reconciliation'],
  ['bar-chart', 'Trusted financial insights'],
  ['handshake', 'Better care through accurate billing'],
];

/** The pulse line at the top of the card: it draws itself once, by a wipe that moves only transforms. */
function PulseLine() {
  return (
    <span className={styles.pulse} aria-hidden="true">
      <span className={styles.pulseWipe}>
        <svg className={styles.pulseLine} viewBox="0 0 104 82" focusable="false">
          <path d="M3 44h22l9-24 14 58 15-74 11 54 6-14h21" />
        </svg>
      </span>
    </span>
  );
}

// The photograph of the reference, when the repository has it: the page stands without it (ADR 0047).
const photographs = import.meta.glob<string>('../assets/brand/signin-hospital.webp', {
  eager: true,
  query: '?url',
  import: 'default',
});
const photograph = Object.values(photographs)[0] ?? null;
const wideQuery = '(min-width: 900px)';

/**
 * The ground of the page (docs/ui/sign-in.png): the blue that runs from navy to the page colour, the photograph under
 * its tint over the left two thirds, the soft curve on the right and the two waves along the bottom. All of it is
 * decoration. The photograph is asked for only on a wide window, where it shows, and fades in once it has decoded, so the
 * first paint never waits for it.
 */
export function SignInBackdrop({ photo = photograph }: { photo?: string | null }) {
  const [wide] = useState(() => window.matchMedia(wideQuery).matches);
  const [decoded, setDecoded] = useState(false);

  return (
    <div className={styles.backdrop} aria-hidden="true">
      {photo !== null && wide && (
        <img
          className={decoded ? styles.photoShown : styles.photo}
          src={photo}
          alt=""
          decoding="async"
          onLoad={() => setDecoded(true)}
        />
      )}
      <div className={styles.tint} />
      <svg className={styles.curve} viewBox="0 0 400 941" preserveAspectRatio="none" focusable="false">
        <path d="M400 0v420C330 330 250 250 120 190 60 160 20 150 0 150 90 120 230 70 400 0Z" />
      </svg>
      <div className={styles.waves}>
        <svg className={styles.waveBack} viewBox="0 0 3344 160" preserveAspectRatio="none" focusable="false">
          <path d="M0 70c280-60 560-60 836 0s556 60 836 0 556-60 836 0 556 60 836 0v90H0Z" />
        </svg>
        <svg className={styles.waveFront} viewBox="0 0 3344 160" preserveAspectRatio="none" focusable="false">
          <path d="M0 100c280 50 560 50 836 0s556-50 836 0 556 50 836 0 556-50 836 0v60H0Z" />
        </svg>
      </div>
    </div>
  );
}
