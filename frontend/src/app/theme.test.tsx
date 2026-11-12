import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider, useTheme } from './theme';

function Probe() {
  const { theme, toggle } = useTheme();
  return (
    <button type="button" onClick={toggle}>
      tema atual: {theme}
    </button>
  );
}

describe('ThemeProvider', () => {
  it('começa claro por padrão, alterna manualmente e persiste a preferência localmente', async () => {
    render(<ThemeProvider><Probe /></ThemeProvider>);
    const user = userEvent.setup();

    expect(document.documentElement.dataset.theme).toBe('light');
    await user.click(screen.getByRole('button'));
    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(localStorage.getItem('hdp-theme')).toBe('dark');

    await user.click(screen.getByRole('button'));
    expect(document.documentElement.dataset.theme).toBe('light');
    expect(localStorage.getItem('hdp-theme')).toBe('light');
  });

  it('restaura a preferência salva', () => {
    localStorage.setItem('hdp-theme', 'dark');
    render(<ThemeProvider><Probe /></ThemeProvider>);
    expect(screen.getByRole('button')).toHaveTextContent('tema atual: dark');
    expect(document.documentElement.dataset.theme).toBe('dark');
  });

  it('ignora valores inválidos salvos', () => {
    localStorage.setItem('hdp-theme', 'roxo');
    render(<ThemeProvider><Probe /></ThemeProvider>);
    expect(screen.getByRole('button')).toHaveTextContent('tema atual: light');
  });
});
