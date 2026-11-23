import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { stubColourScheme } from '../test/colourScheme';
import { themeStorageKey } from './theme';
import { ThemeProvider } from './ThemeProvider';
import { ThemeSelect } from './ThemeSelect';

const html = document.documentElement;

function renderChooser() {
  const user = userEvent.setup();
  render(
    <ThemeProvider>
      <ThemeSelect />
    </ThemeProvider>,
  );

  return { user, select: screen.getByLabelText('Theme') };
}

describe('choosing the theme (ADR 0038, point 7)', () => {
  it('starts from System, which follows the system', () => {
    stubColourScheme(true);
    const { select } = renderChooser();

    expect(select).toHaveValue('system');
    expect(html).toHaveAttribute('data-theme', 'dark');
    expect(html).toHaveAttribute('data-theme-setting', 'system');
  });

  it('applies Light and Dark at once, without a reload, and keeps the choice for the next visit', async () => {
    const { user, select } = renderChooser();

    await user.selectOptions(select, 'Dark');
    expect(html).toHaveAttribute('data-theme', 'dark');
    expect(window.localStorage.getItem(themeStorageKey)).toBe('dark');

    await user.selectOptions(select, 'Light');
    expect(html).toHaveAttribute('data-theme', 'light');
    expect(window.localStorage.getItem(themeStorageKey)).toBe('light');
  });

  it('with System, reacts when the system changes its scheme', async () => {
    const scheme = stubColourScheme(false);
    const { user, select } = renderChooser();
    await user.selectOptions(select, 'System');
    expect(html).toHaveAttribute('data-theme', 'light');

    act(() => scheme.set(true));
    expect(await screen.findByLabelText('Theme')).toHaveValue('system');
    expect(html).toHaveAttribute('data-theme', 'dark');

    act(() => scheme.set(false));
    expect(html).toHaveAttribute('data-theme', 'light');
  });

  it('with Light or Dark, ignores the system', async () => {
    const scheme = stubColourScheme(false);
    const { user, select } = renderChooser();
    await user.selectOptions(select, 'Light');

    act(() => scheme.set(true));

    expect(html).toHaveAttribute('data-theme', 'light');
  });

  it('takes up the stored choice', () => {
    window.localStorage.setItem(themeStorageKey, 'dark');
    const { select } = renderChooser();

    expect(select).toHaveValue('dark');
    expect(html).toHaveAttribute('data-theme', 'dark');
  });
});
