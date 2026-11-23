import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ApiProblem } from '../api/problem';
import { ProblemAlert } from './ProblemAlert';

const refusal: ApiProblem = {
  status: 400,
  errorCode: 'validation_failed',
  title: 'One or more fields are invalid.',
  detail: null,
  fieldErrors: { email: ['Enter an address.'], pageSize: ['The page size is at most 100.'] },
  correlationId: 'b5e0c4b8-3a5f-4a49-9f43-1c1b3c0c7a11',
  retryAfterSeconds: null,
};

describe('the error component (ADR 0038)', () => {
  it("shows the API's message, the field messages not placed elsewhere, and the reference", () => {
    render(<ProblemAlert problem={refusal} placedFields={['email']} />);

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('One or more fields are invalid.');
    expect(alert).toHaveTextContent('The page size is at most 100.');
    expect(alert).not.toHaveTextContent('Enter an address.');
    expect(alert).toHaveTextContent('Reference for support: b5e0c4b8-3a5f-4a49-9f43-1c1b3c0c7a11');
    expect(alert).toHaveAttribute('data-error-code', 'validation_failed');
  });

  it('prefers the detail to the title', () => {
    render(<ProblemAlert problem={{ ...refusal, detail: 'The bill is already posted.', fieldErrors: {} }} />);

    expect(screen.getByRole('alert')).toHaveTextContent('The bill is already posted.');
    expect(screen.getByRole('alert')).not.toHaveTextContent('One or more fields are invalid.');
  });

  it.each([
    [1, 'Try again in 1 second.'],
    [45, 'Try again in 45 seconds.'],
    [60, 'Try again in 1 minute.'],
    [900, 'Try again in 15 minutes.'],
  ])('says to wait %i seconds as "%s"', (seconds, text) => {
    render(<ProblemAlert problem={{ ...refusal, retryAfterSeconds: seconds }} />);

    expect(screen.getByRole('alert')).toHaveTextContent(text);
  });

  it('shows no reference when there is none', () => {
    render(<ProblemAlert problem={{ ...refusal, correlationId: null }} />);

    expect(screen.getByRole('alert')).not.toHaveTextContent('Reference for support');
  });
});
