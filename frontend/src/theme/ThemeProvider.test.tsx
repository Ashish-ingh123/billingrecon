import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { stubColourScheme } from '../test/colourScheme';
import { themeStorageKey } from './theme';
import { ThemeProvider } from './ThemeProvider';
import { ThemeToggle } from './ThemeToggle';

const html = document.documentElement;

function renderChooser() {
  const user = userEvent.setup();
  render(
    <ThemeProvider>
      <ThemeToggle />
    </ThemeProvider>,
  );

  // Since Prompt 9.2 (ADR 0048) the choice is a group of three radio buttons, not a select: the same three settings.
  const choice = (name: 'Light' | 'Dark' | 'System') => screen.getByRole('radio', { name });
  const chosen = () =>
    screen.getAllByRole('radio').find((radio) => (radio as HTMLInputElement).checked) as HTMLInputElement;
  return { user, choice, chosen };
}

describe('choosing the theme (ADR 0038, point 7)', () => {
  it('starts from System, which follows the system', () => {
    stubColourScheme(true);
    const { chosen } = renderChooser();

    expect(chosen().value).toBe('system');
    expect(html).toHaveAttribute('data-theme', 'dark');
    expect(html).toHaveAttribute('data-theme-setting', 'system');
  });

  it('applies Light and Dark at once, without a reload, and keeps the choice for the next visit', async () => {
    const { user, choice } = renderChooser();

    await user.click(choice('Dark'));
    expect(html).toHaveAttribute('data-theme', 'dark');
    expect(window.localStorage.getItem(themeStorageKey)).toBe('dark');

    await user.click(choice('Light'));
    expect(html).toHaveAttribute('data-theme', 'light');
    expect(window.localStorage.getItem(themeStorageKey)).toBe('light');
  });

  it('with System, reacts when the system changes its scheme', async () => {
    const scheme = stubColourScheme(false);
    const { user, choice } = renderChooser();
    await user.click(choice('System'));
    expect(html).toHaveAttribute('data-theme', 'light');

    act(() => scheme.set(true));
    expect(await screen.findByRole('radio', { name: 'System' })).toBeChecked();
    expect(html).toHaveAttribute('data-theme', 'dark');

    act(() => scheme.set(false));
    expect(html).toHaveAttribute('data-theme', 'light');
  });

  it('with Light or Dark, ignores the system', async () => {
    const scheme = stubColourScheme(false);
    const { user, choice } = renderChooser();
    await user.click(choice('Light'));

    act(() => scheme.set(true));

    expect(html).toHaveAttribute('data-theme', 'light');
  });

  it('takes up the stored choice', () => {
    window.localStorage.setItem(themeStorageKey, 'dark');
    const { chosen } = renderChooser();

    expect(chosen().value).toBe('dark');
    expect(html).toHaveAttribute('data-theme', 'dark');
  });
});
