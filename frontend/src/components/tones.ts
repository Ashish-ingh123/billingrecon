import type { IconName } from '../brand/Icon';

/** What a status means at a glance, as a colour of the tokens. */
export type Tone = 'muted' | 'accent' | 'success' | 'warning' | 'danger';

/**
 * The icon of each meaning (ADR 0048): a status is said by its word, its icon and its colour, never the colour alone.
 * One table for every badge of every screen; a test holds it complete.
 */
export const toneIcons: Record<Tone, IconName> = {
  muted: 'dot',
  accent: 'dot',
  success: 'check-circle',
  warning: 'alert',
  danger: 'alert',
};
