import { useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { useCan } from '../auth/permissions';
import buttons from '../components/buttons.module.css';
import { Details } from '../components/Details';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { StatusBadge } from '../components/StatusBadge';
import { useStatusActions } from '../components/useStatusActions';
import { formatDecimal, formatInstant, formatPercent } from '../format';
import {
  comparedServiceItemFields,
  deactivateServiceItem,
  getServiceItem,
  reactivateServiceItem,
  serviceItemKeys,
  useServiceItem,
  type ServiceItem,
} from './api';

/** One service item of the catalogue, and what a user who may change it can do. */
export function ServiceItemPage() {
  const { id = '' } = useParams();
  const item = useServiceItem(id);

  return item.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Service item" back={{ to: '/service-items', label: 'Service items' }} />
      <LoadState error={item.error} what="the service item" />
    </section>
  ) : (
    <ServiceItemView item={item.data} />
  );
}

function ServiceItemView({ item }: { item: ServiceItem }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const canChange = useCan('WriteReferenceData');

  const status = useStatusActions({
    record: item,
    what: 'service item',
    deactivate: () => deactivateServiceItem(client, item),
    reactivate: () => reactivateServiceItem(client, item),
    compared: comparedServiceItemFields,
    reload: () => getServiceItem(client, item.id),
    onChanged: (changed) => {
      queryClient.setQueryData(serviceItemKeys.one(changed.id), changed);
      void queryClient.invalidateQueries({ queryKey: [...serviceItemKeys.all, 'list'] });
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={item.name}
        back={{ to: '/service-items', label: 'Service items' }}
        subtitle={
          <>
            Code {item.code} · <StatusBadge status={item.status} />
          </>
        }
        actions={
          canChange && (
            <>
              <Link className={buttons.secondary} to={`/service-items/${item.id}/edit`}>
                Edit
              </Link>
              {status.actions}
            </>
          )
        }
      />
      {status.notices}
      <Details
        items={[
          ['Category', item.category],
          ['Default unit price', `₹${formatDecimal(item.defaultUnitPrice, 4)}`],
          ['Tax rate', formatPercent(item.taxRatePercent)],
          ['Created', formatInstant(item.createdAtUtc)],
          ['Last changed', formatInstant(item.updatedAtUtc)],
        ]}
      />
      <p className={forms.note}>
        A new bill line starts from this price and tax rate; a line already written keeps its own.
        {item.status === 'Inactive' && ' An inactive item is still listed, read and corrected.'}
      </p>
    </section>
  );
}
