import { useMutation } from '@tanstack/react-query';
import { useEffect, useRef, type ReactNode } from 'react';
import { Link } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useIdempotentRequest } from '../api/useIdempotentRequest';
import { BrandMark } from '../brand/BrandMark';
import buttons from '../components/buttons.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatInstant } from '../format';
import { addressLines, imagePending, recordPrint, useLetterheadImage, type Letterhead, type PrintedKind } from './api';
import styles from './Printing.module.css';

/**
 * The sheet of a printed document (Prompt 10.4b, ADR 0054): the hospital's letterhead, the document's title and which
 * print this is, the document itself, and the place of a signature. On the screen it stands under a bar with the way
 * back and the Print button; on paper the bar and the application around it are gone.
 *
 * Print records the print first, so the page says "Original" for the first print of a document and "Duplicate" for
 * every later one, and only then opens the browser's print dialog. A page printed some other way, before the button was
 * pressed, says that it is an unrecorded copy.
 *
 * Since Prompt 10.4c (ADR 0059) the hospital's logo stands at the head, in place of the product's mark, and its
 * signatory's signature, name and designation at the foot, each image in a box of a fixed size so that nothing moves
 * when it arrives. Print is offered only once the images the letterhead names are here or have failed, and the dialog
 * opens only when the browser has decoded them. A letterhead that names no image prints as it did before.
 */
export function PrintSheet({
  kind,
  id,
  back,
  title,
  letterhead,
  children,
}: {
  kind: PrintedKind;
  id: string;
  back: { to: string; label: string };
  title: string;
  letterhead: Letterhead;
  children: ReactNode;
}) {
  const client = useApiClient();
  const sendOnce = useIdempotentRequest();
  const printing = useMutation({
    mutationFn: () => sendOnce(`${kind}/${id}`, (key) => recordPrint(client, kind, id, key)),
  });
  const record = printing.data;

  const logo = useLetterheadImage('logo', letterhead.logoSha256);
  const signature = useLetterheadImage('signature', letterhead.signatureSha256);
  const waitingForImages =
    imagePending(letterhead.logoSha256, logo) || imagePending(letterhead.signatureSha256, signature);
  const sheet = useRef<HTMLElement>(null);

  // The dialog opens once the page says which print it is, and the browser has decoded the images of the sheet.
  useEffect(() => {
    if (record === undefined) {
      return;
    }

    const images = Array.from(sheet.current?.querySelectorAll('img') ?? []);
    if (images.length === 0) {
      window.print();
      return;
    }

    let current = true;
    void Promise.all(images.map((image) => decoded(image))).then(() => {
      if (current) {
        window.print();
      }
    });
    return () => {
      current = false;
    };
  }, [record]);

  const mark =
    record === undefined
      ? 'Unrecorded copy'
      : record.original
        ? 'Original'
        : `Duplicate (copy ${String(record.copyNumber)})`;

  return (
    <>
      <div className={styles.bar}>
        <Link className={buttons.link} to={back.to}>
          Back to {back.label}
        </Link>
        <button
          type="button"
          className={buttons.primary}
          disabled={printing.isPending || waitingForImages}
          aria-busy={printing.isPending || waitingForImages}
          onClick={() => printing.mutate()}
        >
          {printing.isPending || waitingForImages ? 'Preparing…' : record === undefined ? 'Print' : 'Print again'}
        </button>
      </div>
      <p className={styles.barNote}>
        The first print of a document is marked Original and every later one Duplicate. Use the browser&apos;s dialog to
        print on A4 or to save as PDF.
      </p>
      {printing.error !== null && <ProblemAlert problem={problemOf(printing.error)} />}

      <article ref={sheet} className={styles.sheet} aria-labelledby="sheet-title">
        <header className={styles.letterhead}>
          {letterhead.logoSha256 === null ? (
            <BrandMark className={styles.mark} />
          ) : (
            <div className={styles.logoBox}>
              {logo.data !== undefined && <img className={styles.logo} src={logo.data} alt="" />}
            </div>
          )}
          <div>
            <p className={styles.hospital}>{letterhead.legalName ?? letterhead.name}</p>
            {addressLines(letterhead).map((line) => (
              <p key={line} className={styles.address}>
                {line}
              </p>
            ))}
            {letterhead.gstin !== null && <p className={styles.address}>GSTIN {letterhead.gstin}</p>}
          </div>
        </header>

        <div className={styles.titleRow}>
          <h2 id="sheet-title" className={styles.title}>
            {title}
          </h2>
          <p className={styles.copy} aria-live="polite">
            {mark}
            {record !== undefined && (
              <span className={styles.printedAt}> · printed {formatInstant(record.printedAtUtc)}</span>
            )}
          </p>
        </div>

        {children}

        <footer className={styles.signature}>
          {letterhead.signatureSha256 === null ? (
            <>
              <div className={styles.signatureLine} />
              <p>{letterhead.signatory ?? 'Authorised signatory'}</p>
            </>
          ) : (
            <>
              <p className={styles.signatureLabel}>Authorised signatory</p>
              <div className={styles.signatureBox}>
                {signature.data !== undefined && (
                  <img
                    className={styles.signatureImage}
                    src={signature.data}
                    alt={`Signature of ${letterhead.signatory ?? 'the authorised signatory'}`}
                  />
                )}
              </div>
              <div className={styles.signatureLine} />
              {letterhead.signatory !== null && <p>{letterhead.signatory}</p>}
            </>
          )}
          {letterhead.signatoryDesignation !== null && (
            <p className={styles.address}>{letterhead.signatoryDesignation}</p>
          )}
          <p className={styles.address}>For {letterhead.legalName ?? letterhead.name}</p>
        </footer>
      </article>
    </>
  );
}

/** Resolves when the browser has decoded the image, or cannot: a broken image never holds a print back. */
function decoded(image: HTMLImageElement): Promise<void> {
  return typeof image.decode === 'function' ? image.decode().catch(() => undefined) : Promise.resolve();
}
