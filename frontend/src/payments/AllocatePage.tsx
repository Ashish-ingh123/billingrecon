import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { ApplyToBills } from '../bills/ApplyToBills';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { usePayer } from '../payers/api';
import { allocate, paymentKeys, paymentsChanged, usePaymentWithAllocations, type PaymentWithAllocations } from './api';

/**
 * Applies a payment to the payer's bills (US-31, BR-303, ADR 0041 point 1): the bills that owe something, oldest first,
 * each with its balance due; "Apply oldest first" fills them from the unapplied cash. The form refuses an amount above a
 * bill's balance, a total above the unapplied cash and an empty allocation before sending; the server checks again.
 */
export function AllocatePage() {
  const { id = '' } = useParams();
  const payment = usePaymentWithAllocations(id);

  if (payment.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Allocate" back={{ to: `/payments/${id}`, label: 'Payment' }} />
        <LoadState error={payment.error} what="the payment" />
      </section>
    );
  }

  return <AllocateForm key={payment.data.payment.id} record={payment.data} />;
}

function AllocateForm({ record }: { record: PaymentWithAllocations }) {
  const { payment } = record;
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const payer = usePayer(payment.payerId);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Allocate ${payment.number}`}
        back={{ to: `/payments/${payment.id}`, label: payment.number }}
        // The line is there before the payer has loaded, so nothing moves when the name arrives (ADR 0050, no layout shift).
        subtitle={payer.data === undefined ? 'To the bills of …' : `To the bills of ${payer.data.name}`}
      />
      <ApplyToBills
        payerId={payment.payerId}
        available={payment.unappliedAmount}
        availableLabel="Unapplied cash"
        availableWord="unapplied"
        words={{ submit: 'Allocate', submitting: 'Allocating…', applied: 'Allocated here' }}
        linesField="allocations"
        identity={`POST /payments/${payment.id}/allocations ${payment.version}`}
        send={async (lines, key) => (await allocate(client, payment, lines, key)).data}
        onDone={(data) => {
          queryClient.setQueryData(paymentKeys.one(payment.id), data);
          paymentsChanged(queryClient);
          void navigate(`/payments/${payment.id}`);
        }}
        onConflict={() => {
          paymentsChanged(queryClient);
          void queryClient.invalidateQueries({ queryKey: paymentKeys.one(payment.id) });
        }}
        conflictMessage="Someone else changed this payment or one of these bills first. Nothing was allocated; the payment and the bills are shown as they are now. Check the amounts and allocate again."
        cancelTo={`/payments/${payment.id}`}
      />
    </section>
  );
}
