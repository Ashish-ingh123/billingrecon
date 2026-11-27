import { useQuery } from '@tanstack/react-query';
import { useId } from 'react';
import { answer } from '../../api/call';
import { useApiClient } from '../../api/ApiClientContext';
import { useCan } from '../../auth/permissions';
import listStyles from '../ListControls.module.css';

/**
 * "Made by": the users of the hospital, for a list's `createdBy` (Prompt 12.2; ADR 0063). Only for who may read the
 * users (a TenantAdmin); for anyone else it is not shown and reads nothing, so no role sees more than it did.
 */
export function MakerFilter({ value, onChange }: { value: string; onChange: (userId: string) => void }) {
  const canRead = useCan('ManageTenant');
  const client = useApiClient();
  const id = useId();
  const users = useQuery({
    queryKey: ['users', 'makers'],
    queryFn: ({ signal }) => answer(client.GET('/api/v1/users', { params: { query: { PageSize: 100 } }, signal })),
    enabled: canRead,
    staleTime: 60_000,
  });

  if (!canRead) {
    return null;
  }

  return (
    <div className={listStyles.field}>
      <label htmlFor={id}>Made by</label>
      <select id={id} className={listStyles.input} value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">Anyone</option>
        {(users.data?.items ?? []).map((user) => (
          <option key={user.id} value={user.id}>
            {user.displayName}
          </option>
        ))}
      </select>
    </div>
  );
}
