import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { adjustmentStatusLabels } from '../adjustments/api';
import { billStatuses, billStatusLabels } from '../bills/api';
import { applicationStatusLabels, creditNoteStatusLabels } from '../creditNotes/api';
import { formatDecimal, formatMoney } from '../format';
import { allocationStatusLabels, paymentStatusLabels } from '../payments/api';
import { refundStatusLabels } from '../refunds/api';
import { Banner } from './Banner';
import { DocumentStatus } from './DocumentStatus';
import { documentStatuses, type DocumentKind } from './documentStatuses';
import { SelectField, TextField } from './Field';
import { Money } from './Money';
import { toneIcons } from './tones';
import { TotalsCard } from './TotalsCard';

/** The shared pieces of the money screens (Prompt 9.3, ADR 0049). */
describe('money as it is shown (ADR 0049)', () => {
  it('reads exactly as the screens wrote it before: the rupee sign, Indian grouping, two decimals', () => {
    for (const value of [0, 1, 49.98, 1049.48, 1280000, '731.12']) {
      expect(formatMoney(value)).toBe(`₹${formatDecimal(value)}`);
    }

    render(<Money value={1280000} />);
    expect(screen.getByText('₹12,80,000.00')).toBeInTheDocument();
  });

  it('says a credit or a negative amount by a sign and a word, not by a colour alone', () => {
    const { container, unmount } = render(<Money value={500} credit />);
    expect(container).toHaveTextContent('−₹500.00 credit');
    unmount();

    render(<Money value={-12.5} />);
    expect(screen.getByText('−₹12.50', { exact: false })).toHaveTextContent('−₹12.50 negative');
  });
});

describe('the one mapping of statuses (ADR 0049)', () => {
  it('gives every status of every document a word, a meaning and an icon', () => {
    for (const [kind, statuses] of Object.entries(documentStatuses)) {
      for (const [status, look] of Object.entries(statuses)) {
        expect(look.label, `${kind} ${status}`).not.toBe('');
        expect(Object.keys(toneIcons), `${kind} ${status}`).toContain(look.tone);
        expect(look.icon, `${kind} ${status}`).not.toBe('');
      }
    }

    expect(Object.keys(documentStatuses.bill).sort()).toEqual([...billStatuses].sort());
  });

  it("is what every module's labels are", () => {
    expect(billStatusLabels).toEqual(
      Object.fromEntries(Object.entries(documentStatuses.bill).map(([status, look]) => [status, look.label])),
    );
    expect(creditNoteStatusLabels).toBe(documentStatuses.creditNote);
    expect(applicationStatusLabels).toBe(documentStatuses.creditApplication);
    expect(adjustmentStatusLabels).toBe(documentStatuses.adjustment);
    expect(paymentStatusLabels).toBe(documentStatuses.payment);
    expect(allocationStatusLabels).toBe(documentStatuses.allocation);
    expect(refundStatusLabels).toBe(documentStatuses.refund);
  });

  it('draws each status as its word with an icon', () => {
    for (const kind of Object.keys(documentStatuses) as DocumentKind[]) {
      for (const [status, look] of Object.entries(documentStatuses[kind])) {
        const { container, unmount } = render(
          <DocumentStatus kind={kind} status={status as keyof (typeof documentStatuses)[typeof kind]} />,
        );
        expect(container).toHaveTextContent(look.label);
        expect(container.querySelector('svg')).not.toBeNull();
        unmount();
      }
    }
  });
});

describe('the totals card (ADR 0049)', () => {
  const rows = [
    { label: 'Subtotal', value: 1000 },
    { label: 'Tax', value: 49.98 },
    { label: 'Total', value: 1049.48, strong: true },
  ];

  it('shows the figures it is given, the total among them, under the name it is given', () => {
    render(<TotalsCard label="Totals" rows={rows} />);

    const totals = screen.getByLabelText('Totals');
    expect(totals).toHaveTextContent('Subtotal₹1,000.00Tax₹49.98Total₹1,049.48');
    expect(screen.queryByText('Estimate')).not.toBeInTheDocument();
  });

  it('says that an estimate is one, and why', () => {
    render(<TotalsCard label="Estimated totals" rows={rows} estimate="An estimate until the draft is saved." />);

    expect(screen.getByText('Estimate')).toBeInTheDocument();
    expect(screen.getByText('An estimate until the draft is saved.')).toBeInTheDocument();
  });

  it('marks a figure when it changes, and not when it is first shown', () => {
    const { container, rerender } = render(<TotalsCard rows={rows} />);
    expect(container.querySelectorAll('dd[class]')).toHaveLength(0);

    rerender(<TotalsCard rows={[rows[0]!, rows[1]!, { label: 'Total', value: 1100, strong: true }]} />);

    const marked = container.querySelectorAll('dd[class]');
    expect(marked).toHaveLength(1);
    expect(marked[0]).toHaveTextContent('₹1,100.00');
  });
});

describe('fields with a unit and a required mark (ADR 0049)', () => {
  it('draws the unit inside the field without changing its name', () => {
    render(<TextField id="amount" label="Amount (₹)" leading="₹" />);

    const input = screen.getByLabelText('Amount (₹)');
    expect(input).toHaveAccessibleName('Amount (₹)');
    expect(input.parentElement?.querySelector('[aria-hidden="true"]')).toHaveTextContent('₹');
  });

  it('marks a required field for the eye and for assistive technology, and leaves the checking to the form', () => {
    render(
      <>
        <TextField id="date" label="Bill date" requiredMark />
        <SelectField id="kind" label="Kind" requiredMark>
          <option value="">Choose…</option>
        </SelectField>
      </>,
    );

    for (const name of ['Bill date', 'Kind']) {
      const control = screen.getByLabelText(name);
      expect(control).toHaveAttribute('aria-required', 'true');
      expect(control).not.toHaveAttribute('required');
      expect(control).toHaveAccessibleName(name);
    }
    expect(screen.getAllByText('Required')).toHaveLength(2);
  });
});

describe('the banner (ADR 0049)', () => {
  it('says its state in words, and is announced when it is news', () => {
    render(
      <Banner tone="warning" title="Waiting for approval" live>
        This bill waits for approval.
      </Banner>,
    );

    const banner = screen.getByRole('status');
    expect(within(banner).getByText('Waiting for approval')).toBeInTheDocument();
    expect(within(banner).getByText('This bill waits for approval.')).toBeInTheDocument();
  });
});
