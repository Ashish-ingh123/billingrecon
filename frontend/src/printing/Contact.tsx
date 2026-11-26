import { addressOf, type PatientContact } from './patientContact';

/** The two facts of a patient's contact, each only when there is something to print. */
export function ContactFacts({ contact }: { contact: PatientContact | undefined }) {
  if (contact === undefined) {
    return null;
  }

  const address = addressOf(contact);
  return (
    <>
      {contact.phone !== null && contact.phone !== '' && (
        <div>
          <dt>Phone</dt>
          <dd>{contact.phone}</dd>
        </div>
      )}
      {address !== '' && (
        <div>
          <dt>Address</dt>
          <dd>{address}</dd>
        </div>
      )}
    </>
  );
}
