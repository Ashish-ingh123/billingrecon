import logo from '../assets/brand/ashline-logo.svg';
import styles from './brand.module.css';

/**
 * The company behind the product (ADR 0047): ashline's mark, the file of the repository as it is, and its name set as on
 * the company's site, "ash" at medium weight and "line" at regular weight in the company's violet, on a plate that keeps
 * both legible on the brand's blue. `lead` is what stands before it: "A product of" on the sign-in page.
 */
export function CompanyCredit({ lead }: { lead: string }) {
  return (
    <p className={styles.credit}>
      <span className={styles.creditLead}>{lead}</span>
      <span className={styles.plate}>
        <img className={styles.companyLogo} src={logo} alt="ashline" width="141" height="130" />
        <span className={styles.companyName} aria-hidden="true">
          ash<span className={styles.companyAccent}>line</span>
        </span>
      </span>
    </p>
  );
}
