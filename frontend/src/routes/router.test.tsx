import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { chain, shellResponder } from '@/test/fixtures';
import { installFetch, makeSession, makeUser, renderRoutes } from '@/test/utils';
import { routes } from './router';

// O canal em tempo real não é objeto destes testes: um duplo mínimo evita abrir conexões.
vi.mock('@microsoft/signalr', () => {
  const connection = {
    on: () => {},
    onreconnecting: () => {},
    onreconnected: () => {},
    onclose: () => {},
    start: async () => {},
    stop: async () => {},
  };
  class HubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { return connection; }
  }
  return { HubConnectionBuilder, LogLevel: { Critical: 5 } };
});

const adminData = ({ method, path }: { method: string; path: string }) =>
  method === 'GET' && path === '/users' ? { json: [] } : undefined;

describe('rotas e permissões', () => {
  it('visitante anônimo é levado ao login ao abrir uma rota protegida', async () => {
    installFetch(() => undefined, { session: null });
    renderRoutes(routes, '/tickets');
    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });

  it('usuário autenticado que abre /login volta ao início', async () => {
    installFetch(chain(shellResponder, ({ path }) => (path === '/dashboard/summary' ? { json: { total: 0, open: 0, inProgress: 0, waitingForUser: 0, resolved: 0, closed: 0, cancelled: 0 } } : path === '/tickets' ? { json: { items: [], page: 1, pageSize: 5, totalCount: 0, totalPages: 0 } } : undefined)), {
      session: makeSession(makeUser('REQUESTER', { fullName: 'Ana Lima' })),
    });
    renderRoutes(routes, '/login');
    expect(await screen.findByRole('heading', { name: /Olá, Ana/ })).toBeInTheDocument();
  });

  it.each([
    ['REQUESTER', '/admin/users'],
    ['AGENT', '/admin/users'],
    ['REQUESTER', '/kanban'],
    ['REQUESTER', '/queue'],
    ['AGENT', '/admin/audit'],
  ] as const)('%s acessando %s vê a página 403 e nenhuma chamada de dados é feita', async (role, route) => {
    const { calls } = installFetch(chain(shellResponder, adminData), { session: makeSession(makeUser(role)) });
    renderRoutes(routes, route);

    expect(await screen.findByRole('heading', { name: 'Acesso negado' })).toBeInTheDocument();
    expect(screen.getByText('403')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/users' || c.path === '/audit' || c.path === '/tickets')).toBe(false);
  });

  it('administrador acessa as telas administrativas', async () => {
    installFetch(chain(shellResponder, adminData), { session: makeSession(makeUser('ADMIN')) });
    renderRoutes(routes, '/admin/users');
    expect(await screen.findByRole('heading', { name: 'Usuários' })).toBeInTheDocument();
  });

  it('exibe a página 404 para rotas inexistentes dentro do layout', async () => {
    installFetch(shellResponder, { session: makeSession(makeUser('REQUESTER')) });
    renderRoutes(routes, '/nao-existe');
    expect(await screen.findByRole('heading', { name: 'Página não encontrada' })).toBeInTheDocument();
  });

  it('o menu lateral reflete o papel do usuário', async () => {
    installFetch(shellResponder, { session: makeSession(makeUser('REQUESTER')) });
    renderRoutes(routes, '/nao-existe');
    const nav = await screen.findByRole('navigation', { name: 'Menu principal' });
    expect(nav).toHaveTextContent('Abrir chamado');
    expect(nav).not.toHaveTextContent('Usuários');
    expect(nav).not.toHaveTextContent('Kanban');
  });

  it('o sino mostra a quantidade de notificações não lidas', async () => {
    installFetch(shellResponder, { session: makeSession(makeUser('REQUESTER')) });
    renderRoutes(routes, '/nao-existe');
    expect(await screen.findByRole('button', { name: 'Notificações: 3 não lidas' })).toBeInTheDocument();
  });
});
