import type { ReactNode, Ref } from 'react';
import { BrandMark } from '../brand/BrandMark';
import { addressLines, type Letterhead } from './api';
import styles from './Printing.module.css';
import type { PrintLayout } from './settings';

const paperClass = { A4: styles.paperA4, A5: styles.paperA5, Roll80: styles.paperRoll80 } as const;
const positionClass = { Left: styles.logoLeft, Centre: styles.logoCentre, Right: styles.logoRight } as const;
const sizeClass = { Small: styles.logoSmall, Medium: styles.logoMedium, Large: styles.logoLarge } as const;

const classes = (...names: (string | undefined | false)[]) => names.filter(Boolean).join(' ');

/**
 * A sheet as it is printed (ADR 0054 as amended, ADR 0059): the head, the title with which print this is, the document,
 * the terms, the signature block and the footer line, laid out by the hospital's settings for this kind of document.
 * The printed page and the preview of the settings both draw this, so what the administrator sees is what prints.
 *
 * The settings change the layout only. Every class is a fixed one of the stylesheet; the paper's margins and the space
 * of pre-printed stationery are numbers, and reach the page through `useLayoutRules`, never through a `style` here.
 */
export function SheetFrame({
  title,
  mark,
  letterhead,
  layout,
  logo,
  signature,
  preview = false,
  frameRef,
  children,
}: {
  title: string;
  /** Which print this is: "Original", "Duplicate (copy 2)", "Unrecorded copy", or "Preview". */
  mark: ReactNode;
  letterhead: Letterhead;
  layout: PrintLayout;
  /** The logo as a `data:` URL, once it is here. */
  logo?: string | undefined;
  /** The signature as a `data:` URL, once it is here. */
  signature?: string | undefined;
  preview?: boolean;
  frameRef?: Ref<HTMLElement>;
  children: ReactNode;
}) {
  const hospital = letterhead.legalName ?? letterhead.name;

  return (
    <article
      ref={frameRef}
      className={classes(
        styles.sheet,
        paperClass[layout.paper],
        preview && styles.preview,
        preview && 'print-sheet-preview',
      )}
      aria-labelledby="sheet-title"
      // A preview is paper on a screen: it takes the tokens of print in either theme (tokens.css).
      data-paper={preview ? '' : undefined}
    >
      {layout.prePrintedStationery ? (
        // The paper carries the hospital's head already: the sheet leaves its place empty.
        <div className={classes(styles.stationery, 'print-stationery-space')} data-testid="stationery-space" />
      ) : (
        <header className={classes(styles.letterhead, positionClass[layout.logoPosition])}>
          {letterhead.logoSha256 === null ? (
            <BrandMark className={styles.mark} />
          ) : (
            <div className={classes(styles.logoBox, sizeClass[layout.logoSize])}>
              {logo !== undefined && <img className={styles.logo} src={logo} alt="" />}
            </div>
          )}
          <div>
            <p className={styles.hospital}>{hospital}</p>
            {addressLines(letterhead).map((line) => (
              <p key={line} className={styles.address}>
                {line}
              </p>
            ))}
            {letterhead.gstin !== null && <p className={styles.address}>GSTIN {letterhead.gstin}</p>}
          </div>
        </header>
      )}

      <div className={styles.titleRow}>
        <h2 id="sheet-title" className={styles.title}>
          {title}
        </h2>
        <p className={styles.copy} aria-live="polite">
          {mark}
        </p>
      </div>

      {children}

      {layout.terms !== null && layout.terms !== '' && (
        <div className={styles.terms}>
          <h3>Terms</h3>
          <p>{layout.terms}</p>
        </div>
      )}

      {layout.showSignatureBlock && (
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
                {signature !== undefined && (
                  <img
                    className={styles.signatureImage}
                    src={signature}
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
          <p className={styles.address}>For {hospital}</p>
        </footer>
      )}

      {layout.footerLine !== null && layout.footerLine !== '' && (
        <p className={styles.footerLine}>{layout.footerLine}</p>
      )}
    </article>
  );
}
