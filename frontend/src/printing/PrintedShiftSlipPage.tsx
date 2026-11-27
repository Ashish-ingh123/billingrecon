import { useRef } from 'react';
import { Link, useParams } from 'react-router';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate } from '../format';
import { shiftStatusLabels, useShift, type CashierShift } from '../shifts/api';
import { imagePending, useLetterhead, useLetterheadImage, type Letterhead } from './api';
import styles from './Printing.module.css';
import { paperLabels, settingsOf, useLayoutRules, usePrintSettings, type PrintLayout } from './settings';
import { SheetFrame } from './SheetFrame';
import { ShiftSlipBody } from './ShiftSlipBody';

/**
 * The closing slip of a shift (Prompt 11.2b, ADR 0060): printed once the shift is counted, on the hospital's letterhead
 * and by its print settings for a closing slip. It is not a financial document, so its print is not recorded and it is
 * neither an original nor a duplicate: the slip says where the shift stands, closed or waiting for approval.
 */
export function PrintedShiftSlipPage() {
  const { id = '' } = useParams();
  const shift = useShift(id);
  const letterhead = useLetterhead();
  const settings = usePrintSettings();

  if (shift.data === undefined || letterhead.data === undefined || settings.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Print a closing slip" back={{ to: `/shifts/${id}`, label: 'the shift' }} />
        <LoadState error={shift.error ?? letterhead.error ?? settings.error} what="the shift" />
      </section>
    );
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Print the closing slip of ${formatDate(shift.data.businessDate)}`} />
      {shift.data.status === 'Open' ? (
        <Banner tone="warning" title="This shift has no closing slip yet">
          The shift is open: its slip is printed once its drawer is counted.
        </Banner>
      ) : (
        <SlipSheet
          shift={shift.data}
          letterhead={letterhead.data}
          layout={settingsOf(settings.data, 'ShiftClosing').layout}
        />
      )}
    </section>
  );
}

function SlipSheet({
  shift,
  letterhead,
  layout,
}: {
  shift: CashierShift;
  letterhead: Letterhead;
  layout: PrintLayout;
}) {
  const logoSha256 = layout.prePrintedStationery ? null : letterhead.logoSha256;
  const signatureSha256 = layout.showSignatureBlock ? letterhead.signatureSha256 : null;
  const logo = useLetterheadImage('logo', logoSha256);
  const signature = useLetterheadImage('signature', signatureSha256);
  const waiting = imagePending(logoSha256, logo) || imagePending(signatureSha256, signature);
  const sheet = useRef<HTMLElement>(null);
  useLayoutRules(layout, 'print');

  return (
    <>
      <div className={styles.bar}>
        <Link className={buttons.link} to={`/shifts/${shift.id}`}>
          Back to the shift
        </Link>
        <button
          type="button"
          className={buttons.primary}
          disabled={waiting}
          aria-busy={waiting}
          onClick={() => window.print()}
        >
          {waiting ? 'Preparing…' : 'Print'}
        </button>
      </div>
      <p className={styles.barNote}>
        A closing slip is a record of the count, not a financial document: its print is not numbered. Use the
        browser&apos;s dialog to print on {layout.paper === 'Roll80' ? 'the 80 mm roll' : paperLabels[layout.paper]} or
        to save as PDF.
      </p>
      <SheetFrame
        frameRef={sheet}
        title="Shift Closing Slip"
        letterhead={letterhead}
        layout={layout}
        logo={logo.data}
        signature={signature.data}
        mark={shiftStatusLabels[shift.status].label}
      >
        <ShiftSlipBody shift={shift} layout={layout} />
      </SheetFrame>
    </>
  );
}
