/** The phone and the address of a patient, as a sheet prints them when its settings ask (ADR 0059). */
export interface PatientContact {
  phone: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  state: string | null;
  pinCode: string | null;
}

/** The address on one line, of the parts that are set. */
export function addressOf(contact: PatientContact): string {
  const place = [contact.city, contact.state].filter((part) => part !== null && part !== '').join(', ');
  return [contact.addressLine1 ?? '', contact.addressLine2 ?? '', [place, contact.pinCode ?? ''].join(' ').trim()]
    .filter((part) => part !== '')
    .join(', ');
}
