import { Link } from 'react-router';
import { AdministrationTabs } from '../administration/AdministrationTabs';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { channelKindLabels, useChannels, type SettlementChannel } from './api';

/**
 * The hospital's settlement channels (Prompt 13.1c, BR-1001; ADR 0065): its bank accounts, each with the column mapping
 * its statement is read with, and its payment-gateway accounts, each with the bank it settles into. Set up by a
 * TenantAdmin, under Users and settings; the statements themselves are under Reconciliation.
 */
export function ChannelsPage() {
  const channels = useChannels();
  const all = channels.data ?? [];
  const nameOf = (id: string | null | undefined) => all.find((channel) => channel.id === id)?.name ?? '—';

  const columns: readonly TableColumn<SettlementChannel>[] = [
    {
      id: 'name',
      header: 'Name',
      fixed: true,
      cell: (channel) => <Link to={`/administration/channels/${channel.id}`}>{channel.name}</Link>,
    },
    { id: 'kind', header: 'Kind', cell: (channel) => channelKindLabels[channel.kind] },
    {
      id: 'account',
      header: 'Account',
      cell: (channel) =>
        channel.accountLabel === null || channel.accountLabel === undefined ? '—' : `…${channel.accountLabel}`,
    },
    {
      id: 'settles',
      header: 'Settles into',
      cell: (channel) => (channel.kind === 'Gateway' ? nameOf(channel.bankChannelId) : '—'),
    },
    { id: 'window', header: 'Date window', numeric: true, cell: (channel) => `${String(channel.dateWindowDays)} days` },
    {
      id: 'status',
      header: 'Status',
      fixed: true,
      cell: (channel) =>
        channel.status === 'Active' ? <Badge tone="success">Active</Badge> : <Badge tone="muted">Inactive</Badge>,
    },
  ];

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Users and settings"
        subtitle="The hospital's bank and gateway accounts, whose statements are reconciled with the payments."
        actions={
          <Link className={buttons.primary} to="/administration/channels/new">
            New channel
          </Link>
        }
      />
      <AdministrationTabs />
      {channels.data === undefined ? (
        <LoadState error={channels.error} what="settlement channels" />
      ) : all.length === 0 ? (
        <EmptyState icon="building">
          No channel is set up yet. Add the bank account your counter&apos;s UPI, card and transfer money reaches.
        </EmptyState>
      ) : (
        <DataTable
          tableId="settlement-channels"
          label="Settlement channels"
          columns={columns}
          rows={all}
          keyOf={(channel) => channel.id}
          busy={channels.isFetching}
        />
      )}
    </section>
  );
}
