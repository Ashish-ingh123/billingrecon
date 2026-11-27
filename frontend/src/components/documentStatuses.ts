import type { components } from '../api/schema';
import type { IconName } from '../brand/Icon';
import type { Tone } from './tones';

type Schemas = components['schemas'];

/** How a status is said: its word, the colour of its meaning and its icon; never the colour alone (ADR 0049). */
export interface StatusLook {
  label: string;
  tone: Tone;
  icon: IconName;
}

const draft: StatusLook = { label: 'Draft', tone: 'muted', icon: 'pencil' };
const waitingForApproval: StatusLook = { label: 'Waiting for approval', tone: 'warning', icon: 'clock' };
const posted: StatusLook = { label: 'Posted', tone: 'accent', icon: 'stamp' };
const voided: StatusLook = { label: 'Voided', tone: 'danger', icon: 'ban' };
const applied: StatusLook = { label: 'Applied', tone: 'success', icon: 'check-circle' };
const waitingForReversal: StatusLook = { label: 'Waiting for reversal', tone: 'warning', icon: 'clock' };
const reversed: StatusLook = { label: 'Reversed', tone: 'danger', icon: 'undo' };

/**
 * The one mapping of every status of every document (ADR 0049, Prompt 9.3): each module's labels are read from here, so
 * a status reads the same on its list, its page, the inbox and the reports. A test holds every status of the API's
 * document to an entry.
 */
export const documentStatuses = {
  bill: {
    Draft: draft,
    PendingApproval: waitingForApproval,
    Posted: posted,
    PartiallyPaid: { label: 'Partly paid', tone: 'warning', icon: 'half' },
    Settled: { label: 'Settled', tone: 'success', icon: 'check-circle' },
    Voided: voided,
  },
  creditNote: { Draft: draft, PendingApproval: waitingForApproval, Posted: posted, Voided: voided },
  creditApplication: { Active: applied, PendingReversal: waitingForReversal, Reversed: reversed },
  adjustment: { Draft: draft, PendingApproval: waitingForApproval, Posted: posted, Voided: voided },
  payment: { Posted: posted, PendingReversal: waitingForReversal, Reversed: reversed },
  allocation: { Active: applied, PendingReversal: waitingForReversal, Reversed: reversed },
  refund: { Draft: draft, PendingApproval: waitingForApproval, Posted: posted, Voided: voided },
  shift: {
    Open: { label: 'Open', tone: 'success', icon: 'clock' },
    PendingApproval: waitingForApproval,
    Closed: { label: 'Closed', tone: 'muted', icon: 'check-circle' },
  },
  record: {
    Active: { label: 'Active', tone: 'success', icon: 'check-circle' },
    Inactive: { label: 'Inactive', tone: 'muted', icon: 'ban' },
  },
} satisfies {
  bill: Record<Schemas['BillStatus'], StatusLook>;
  creditNote: Record<Schemas['CreditNoteStatus'], StatusLook>;
  creditApplication: Record<Schemas['CreditNoteApplicationDto']['status'], StatusLook>;
  adjustment: Record<Schemas['AdjustmentStatus'], StatusLook>;
  payment: Record<Schemas['PaymentStatus'], StatusLook>;
  allocation: Record<Schemas['PaymentAllocationDto']['status'], StatusLook>;
  refund: Record<Schemas['RefundStatus'], StatusLook>;
  shift: Record<Schemas['CashierShiftStatus'], StatusLook>;
  record: Record<'Active' | 'Inactive', StatusLook>;
};

export type DocumentKind = keyof typeof documentStatuses;
