import lockup from '../assets/brand/ashvanta-lockup.svg';
import mark from '../assets/brand/ashvanta-mark.svg';
import styles from './brand.module.css';

/** The width from which the sign-in page has room for the whole logo, its tagline legible (ADR 0047, amended). */
const wideMedia = '(min-width: 1200px)';

/**
 * The company behind the product (ADR 0047, amended 2026-11-28): Ashvanta Technologies, on a white plate that is the same
 * in both themes, because its logo is drawn for a white ground. At a small size the logo's own "TECHNOLOGIES" and tagline
 * shrink past reading, so the credit is the mark, the file as it is, with "Ashvanta" beside it as text, sharp at any size
 * (the owner, 2026-11-28: no "Technologies" when the room is small). With `wide` (the sign-in page) a window of 1200 px
 * or more shows the whole logo instead, large enough for its tagline. The plate is one image to assistive technology,
 * named once, "Ashvanta Technologies".
 * `lead` is what stands before it: "A product of" on the sign-in page, "BillingRecon by" at the foot of the sidebar.
 */
export function CompanyCredit({ lead, wide = false }: { lead: string; wide?: boolean }) {
  return (
    <p className={styles.credit}>
      <span className={styles.creditLead}>{lead}</span>
      <span className={styles.plate} data-wide={wide || undefined} role="img" aria-label="Ashvanta Technologies">
        <picture>
          {wide && <source media={wideMedia} srcSet={lockup} width="508" height="134" />}
          <img className={styles.companyLogo} src={mark} alt="" width="160" height="160" />
        </picture>
        <span className={styles.companyName} aria-hidden="true">
          Ashvanta
        </span>
      </span>
    </p>
  );
}

/** The company's mark alone, for the collapsed sidebar (ADR 0048, amended 2026-11-28); the credit names the company. */
export function CompanyMark({ className }: { className?: string }) {
  return <img className={className} src={mark} alt="" width="160" height="160" />;
}
