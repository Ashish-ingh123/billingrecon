import styles from './brand.module.css';

/**
 * The BillingRecon mark (ADR 0047; docs/ui/sign-in.png): a cross with rounded arms in the brand's bright blue and a
 * darker cross inside it. Drawn here from the tokens, so it holds in both themes. Decoration beside the name.
 */
export function BrandMark({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 48 48" width="1em" height="1em" aria-hidden="true" focusable="false">
      <g className={styles.markOuter}>
        <rect x="15" y="2" width="18" height="44" rx="8" />
        <rect x="2" y="15" width="44" height="18" rx="8" />
      </g>
      <g className={styles.markInner}>
        <rect x="21" y="12.5" width="6" height="23" rx="2.5" />
        <rect x="12.5" y="21" width="23" height="6" rx="2.5" />
      </g>
    </svg>
  );
}
