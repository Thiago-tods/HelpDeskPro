import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router-dom';
import { installFetch, makeSession, makeUser, renderWithProviders } from '@/test/utils';
import LoginPage from './LoginPage';

function renderLogin() {
  return renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<h1>Página inicial</h1>} />
    </Routes>,
    { route: '/login', gate: false },
  );
}

describe('LoginPage', () => {
  it('valida campos obrigatórios e e-mail inválido sem chamar a API', async () => {
    const { calls } = installFetch(() => undefined);
    renderLogin();
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Entrar' }));
    expect(await screen.findByText('Informe seu e-mail.')).toBeInTheDocument();
    expect(screen.getByText('Informe sua senha.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('E-mail'), 'nao-e-email');
    await user.type(screen.getByLabelText('Senha'), 'qualquer');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));
    expect(await screen.findByText('Informe um e-mail válido.')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/auth/login')).toBe(false);
  });

  it('envia as credenciais e vai para a página inicial quando autenticado', async () => {
    const user = makeUser('REQUESTER');
    const { requests } = installFetch(({ method, path }) =>
      method === 'POST' && path === '/auth/login' ? { json: makeSession(user) } : undefined,
    );
    renderLogin();
    const ui = userEvent.setup();

    await ui.type(await screen.findByLabelText('E-mail'), 'ana@nexora.example');
    await ui.type(screen.getByLabelText('Senha'), 'Senha-De-Teste-1!');
    await ui.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(await screen.findByRole('heading', { name: 'Página inicial' })).toBeInTheDocument();
    const [login] = requests('POST', '/auth/login');
    expect(login.body).toEqual({ email: 'ana@nexora.example', password: 'Senha-De-Teste-1!' });
  });

  it('mostra mensagem genérica quando as credenciais são recusadas', async () => {
    installFetch(({ method, path }) => (method === 'POST' && path === '/auth/login' ? { status: 401, json: { title: 'Credenciais inválidas' } } : undefined));
    renderLogin();
    const ui = userEvent.setup();

    await ui.type(await screen.findByLabelText('E-mail'), 'ana@nexora.example');
    await ui.type(screen.getByLabelText('Senha'), 'errada');
    await ui.click(screen.getByRole('button', { name: 'Entrar' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('E-mail ou senha incorretos');
    expect(screen.queryByRole('heading', { name: 'Página inicial' })).not.toBeInTheDocument();
  });

  it('informa limite de tentativas em 429', async () => {
    installFetch(({ method, path }) => (method === 'POST' && path === '/auth/login' ? { status: 429, json: { title: 'Muitas tentativas' } } : undefined));
    renderLogin();
    const ui = userEvent.setup();

    await ui.type(await screen.findByLabelText('E-mail'), 'ana@nexora.example');
    await ui.type(screen.getByLabelText('Senha'), 'x');
    await ui.click(screen.getByRole('button', { name: 'Entrar' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Muitas tentativas'));
  });
});
