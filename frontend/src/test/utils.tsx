import type { ReactElement, ReactNode } from 'react';
import { render } from '@testing-library/react';
import { createMemoryRouter, MemoryRouter, RouterProvider, type RouteObject } from 'react-router-dom';
import { vi } from 'vitest';
import { useAuth } from '@/app/auth';
import { AppProviders } from '@/app/providers';
import { tokenStore } from '@/lib/api';
import type { AuthSession, Role, User } from '@/types/api';

export interface MockResponse {
  status?: number;
  json?: unknown;
}

export type Responder = (call: { method: string; path: string; url: URL; body: unknown }) => MockResponse | undefined;

export interface FetchCall {
  method: string;
  path: string;
  url: URL;
  body: unknown;
  headers: Record<string, string>;
}

export function makeUser(role: Role, overrides: Partial<User> = {}): User {
  return { id: `user-${role}`, fullName: 'Pessoa Teste', email: 'pessoa@teste.example', role, isActive: true, ...overrides };
}

export function makeSession(user: User): AuthSession {
  return { accessToken: 'token-de-teste', expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(), user };
}

/**
 * Substitui o fetch global por um respondedor controlado pelo teste. Chamadas sem resposta definida
 * falham de forma explícita (404), para que nenhuma requisição "escape" silenciosamente.
 */
export function installFetch(responder: Responder, options: { session?: AuthSession | null } = {}) {
  const calls: FetchCall[] = [];
  tokenStore.set(null);

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost');
    const path = url.pathname.replace(/^\/api\/v1/, '');
    const method = init?.method ?? 'GET';
    let body: unknown;
    if (typeof init?.body === 'string') body = JSON.parse(init.body);
    else if (init?.body instanceof FormData) body = init.body;
    const headers = (init?.headers ?? {}) as Record<string, string>;
    calls.push({ method, path, url, body, headers });

    let result: MockResponse | undefined;
    if (method === 'POST' && path === '/auth/refresh') {
      result = options.session ? { json: options.session } : { status: 401, json: { title: 'Sessão inválida' } };
    } else {
      result = responder({ method, path, url, body });
    }
    const status = result?.status ?? (result ? 200 : 404);
    const payload = result ? result.json : { title: `Sem mock para ${method} ${path}` };
    return new Response(status === 204 || payload === undefined ? null : JSON.stringify(payload), {
      status,
      headers: { 'Content-Type': 'application/json' },
    });
  });

  vi.stubGlobal('fetch', fetchMock);
  return { calls, fetchMock, requests: (method: string, path: string) => calls.filter((c) => c.method === method && c.path === path) };
}

/** Reproduz o papel do RequireAuth: só monta a página protegida quando a sessão foi restaurada. */
function AuthGate({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  return status === 'authenticated' ? children : null;
}

/** Renderiza dentro de todos os providers da aplicação (tema, toasts, React Query, autenticação). */
export function renderWithProviders(ui: ReactElement, { route = '/', gate = true }: { route?: string; gate?: boolean } = {}) {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={[route]}>{gate ? <AuthGate>{ui}</AuthGate> : ui}</MemoryRouter>
    </AppProviders>,
  );
}

export function renderRoutes(routes: RouteObject[], route: string) {
  const router = createMemoryRouter(routes, { initialEntries: [route] });
  const view = render(
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>,
  );
  return { router, ...view };
}

export const paged = <T,>(items: T[], extra: Record<string, number> = {}) => ({
  items,
  page: 1,
  pageSize: 15,
  totalCount: items.length,
  totalPages: 1,
  ...extra,
});
