import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { useLetterheadImage, type LetterheadImageKind } from '../printing/api';
import {
  letterheadImageRefusal,
  letterheadImageRules,
  removeLetterheadImage,
  uploadLetterheadImage,
  userKeys,
  type TenantSettings,
} from './api';
import styles from './Administration.module.css';

/**
 * The hospital's logo and its signatory's signature (Prompt 10.4c, ADR 0059): each is uploaded, shown as it will
 * print, replaced by choosing another file and removed. They are saved at once, under the version the settings and the
 * letterhead share, so a save here is told to the page, which gives the two forms the new version.
 */
export function LetterheadImages({ settings, onSaved }: { settings: TenantSettings; onSaved: () => void }) {
  return (
    <section className={forms.section} aria-labelledby="letterhead-images-title">
      <h2 id="letterhead-images-title">Logo and signature</h2>
      <p className={forms.note}>
        The logo is printed at the head of every bill, receipt and refund voucher, and the signature at its foot, above
        the signatory&apos;s name and designation. A PNG, JPEG or WebP image; an SVG is not accepted. Each is saved as
        soon as it is chosen.
      </p>
      <div className={styles.images}>
        <ImageCard
          kind="logo"
          title="Logo"
          sha256={settings.letterhead.logoSha256}
          settings={settings}
          onSaved={onSaved}
        />
        <ImageCard
          kind="signature"
          title="Signature"
          sha256={settings.letterhead.signatureSha256}
          settings={settings}
          onSaved={onSaved}
        />
      </div>
    </section>
  );
}

function ImageCard({
  kind,
  title,
  sha256,
  settings,
  onSaved,
}: {
  kind: LetterheadImageKind;
  title: string;
  sha256: string | null;
  settings: TenantSettings;
  onSaved: () => void;
}) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const inputId = useId();
  const hintId = useId();
  const errorId = useId();
  const image = useLetterheadImage(kind, sha256);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [done, setDone] = useState<'saved' | 'removed' | null>(null);
  const rules = letterheadImageRules[kind];
  const name = title.toLowerCase();

  const changed = (next: TenantSettings, what: 'saved' | 'removed') => {
    queryClient.setQueryData(userKeys.settings, next);
    void queryClient.invalidateQueries({ queryKey: ['letterhead'] });
    setDone(what);
    onSaved();
  };
  const uploading = useMutation({
    mutationFn: (file: File) => uploadLetterheadImage(client, kind, settings.version, file),
    onMutate: () => setDone(null),
    onSuccess: (next) => changed(next, 'saved'),
  });
  const removing = useMutation({
    mutationFn: () => removeLetterheadImage(client, kind, settings.version),
    onMutate: () => {
      setDone(null);
      setRefusal(null);
      uploading.reset();
    },
    onSuccess: (next) => changed(next, 'removed'),
  });
  const busy = uploading.isPending || removing.isPending;
  const failure = uploading.error ?? removing.error;

  return (
    <div className={styles.imageCard}>
      <h3>{title}</h3>
      <div className={styles.imagePreview} data-kind={kind}>
        {sha256 === null ? (
          <p className={styles.imageNone}>No {name} yet</p>
        ) : image.data !== undefined ? (
          <img src={image.data} alt={`The ${name} as it is printed`} />
        ) : image.error !== null ? (
          <p className={styles.imageNone}>The {name} could not be shown</p>
        ) : (
          <p className={styles.imageNone}>Loading…</p>
        )}
      </div>
      {failure !== null && <ProblemAlert problem={problemOf(failure)} />}
      {done !== null && (
        <Banner tone="success" title={done === 'saved' ? 'Saved' : 'Removed'} live>
          {done === 'saved'
            ? `Documents printed from now on carry this ${name}.`
            : `Documents printed from now on carry no ${name}.`}
        </Banner>
      )}
      <div className={styles.imageField}>
        <label htmlFor={inputId}>{sha256 === null ? `Choose a ${name}` : `Replace the ${name}`}</label>
        <input
          id={inputId}
          type="file"
          accept={rules.types.join(',')}
          disabled={busy}
          aria-describedby={refusal === null ? hintId : `${hintId} ${errorId}`}
          aria-invalid={refusal !== null}
          onChange={(event) => {
            const file = event.target.files?.[0];
            // The same file can be chosen again after a refusal.
            event.target.value = '';
            if (file === undefined) {
              return;
            }

            const refused = letterheadImageRefusal(kind, file);
            setRefusal(refused);
            uploading.reset();
            removing.reset();
            if (refused === null) {
              uploading.mutate(file);
            } else {
              setDone(null);
            }
          }}
        />
        <p id={hintId} className={styles.imageHint}>
          PNG, JPEG or WebP, at most {String(rules.maxKb)} KB and 2000 pixels a side.
        </p>
        {refusal !== null && (
          <p id={errorId} className={styles.imageError} role="alert">
            {refusal}
          </p>
        )}
      </div>
      {sha256 !== null && (
        <div className={forms.actions}>
          <button
            type="button"
            className={buttons.secondary}
            disabled={busy}
            aria-busy={removing.isPending}
            onClick={() => removing.mutate()}
          >
            {removing.isPending ? 'Removing…' : `Remove the ${name}`}
          </button>
        </div>
      )}
      {uploading.isPending && (
        <p className={styles.imageHint} role="status">
          Saving the {name}…
        </p>
      )}
    </div>
  );
}
