import { Badge } from './Badge';
import { documentStatuses, type DocumentKind } from './documentStatuses';

/** The status of a document, from the one mapping (ADR 0049): its word, its icon and its colour. */
export function DocumentStatus<K extends DocumentKind>({
  kind,
  status,
}: {
  kind: K;
  status: keyof (typeof documentStatuses)[K];
}) {
  const look = (documentStatuses[kind] as Record<PropertyKey, (typeof documentStatuses)['bill']['Draft']>)[status];
  return (
    <Badge tone={look.tone} icon={look.icon}>
      {look.label}
    </Badge>
  );
}
