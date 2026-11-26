import { useMutation } from '@tanstack/react-query';
import { useEffect, useRef, type ReactNode } from 'react';
import { Link } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useIdempotentRequest } from '../api/useIdempotentRequest';
import buttons from '../components/buttons.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatInstant } from '../format';
import { imagePending, recordPrint, useLetterheadImage, type Letterhead, type PrintedKind } from './api';
import styles from './Printing.module.css';
import { paperLabels, useLayoutRules, type PrintLayout } from './settings';
import { SheetFrame } from './SheetFrame';

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
 * opens only when the browser has decoded them. The sheet is laid out by the hospital's print settings for this kind
 * of document (`SheetFrame`), which also give the printer its paper and margins; a hospital with no settings and no
 * image prints as it did before.
 */
export function PrintSheet({
  kind,
  id,
  back,
  title,
  letterhead,
  layout,
  waiting = false,
  children,
}: {
  kind: PrintedKind;
  id: string;
  back: { to: string; label: string };
  title: string;
  letterhead: Letterhead;
  layout: PrintLayout;
  /** Whether something the settings ask the sheet to show is still being read: Print waits for it as for the images. */
  waiting?: boolean;
  children: ReactNode;
}) {
  const client = useApiClient();
  const sendOnce = useIdempotentRequest();
  const printing = useMutation({
    mutationFn: () => sendOnce(`${kind}/${id}`, (key) => recordPrint(client, kind, id, key)),
  });
  const record = printing.data;

  // A sheet on pre-printed stationery shows no logo; a sheet without its signature block shows no signature.
  const logoSha256 = layout.prePrintedStationery ? null : letterhead.logoSha256;
  const signatureSha256 = layout.showSignatureBlock ? letterhead.signatureSha256 : null;
  const logo = useLetterheadImage('logo', logoSha256);
  const signature = useLetterheadImage('signature', signatureSha256);
  const waitingForImages = waiting || imagePending(logoSha256, logo) || imagePending(signatureSha256, signature);
  const sheet = useRef<HTMLElement>(null);
  useLayoutRules(layout, 'print');

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
        print on {layout.paper === 'Roll80' ? 'the 80 mm roll' : paperLabels[layout.paper]} or to save as PDF.
      </p>
      {printing.error !== null && <ProblemAlert problem={problemOf(printing.error)} />}

      <SheetFrame
        frameRef={sheet}
        title={title}
        letterhead={letterhead}
        layout={layout}
        logo={logo.data}
        signature={signature.data}
        mark={
          <>
            {mark}
            {record !== undefined && (
              <span className={styles.printedAt}> · printed {formatInstant(record.printedAtUtc)}</span>
            )}
          </>
        }
      >
        {children}
      </SheetFrame>
    </>
  );
}

/** Resolves when the browser has decoded the image, or cannot: a broken image never holds a print back. */
function decoded(image: HTMLImageElement): Promise<void> {
  return typeof image.decode === 'function' ? image.decode().catch(() => undefined) : Promise.resolve();
}
