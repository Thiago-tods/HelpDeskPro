import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, messageOf, refreshSession, tokenStore } from './api';

const session = (token: string) => ({
  accessToken: token,
  expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  user: { id: '1', fullName: 'Teste', email: 't@t.com', role: 'ADMIN', isActive: true },
});

const json = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

afterEach(() => {
  tokenStore.set(null);
  tokenStore.onExpired(null);
});

describe('api', () => {
  it('envia o token em memória e o cabeçalho anti-CSRF', async () => {
    tokenStore.set(session('abc'));
    const fetchMock = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => json(200, { ok: true }));
    vi.stubGlobal('fetch', fetchMock);

    await api('/tickets', { query: { page: 2, search: '', overdue: false, status: 'NEW' } });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe('/api/v1/tickets?page=2&status=NEW'); // vazios e false são omitidos
    expect((init!.headers as Record<string, string>).Authorization).toBe('Bearer abc');
    expect((init!.headers as Record<string, string>)['X-Requested-With']).toBe('XMLHttpRequest');
    expect(init!.credentials).toBe('include');
  });

  it('não define Content-Type em FormData (o navegador gera o boundary)', async () => {
    tokenStore.set(session('abc'));
    const fetchMock = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => json(200, {}));
    vi.stubGlobal('fetch', fetchMock);

    await api('/tickets/1/attachments', { method: 'POST', body: new FormData() });

    const init = fetchMock.mock.calls[0]![1]!;
    expect((init.headers as Record<string, string>)['Content-Type']).toBeUndefined();
  });

  it('em 401 renova a sessão uma vez e repete a requisição com o novo token', async () => {
    tokenStore.set(session('velho'));
    const fetchMock = vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      if (String(url).endsWith('/auth/refresh')) return json(200, session('novo'));
      const auth = ((init?.headers ?? {}) as Record<string, string>).Authorization;
      return auth === 'Bearer novo' ? json(200, { dado: 1 }) : json(401, { title: 'Não autorizado' });
    });
    vi.stubGlobal('fetch', fetchMock);

    await expect(api('/tickets')).resolves.toEqual({ dado: 1 });
    expect(tokenStore.get()).toBe('novo');
    expect(fetchMock).toHaveBeenCalledTimes(3); // original, refresh, repetição
  });

  it('chamadas simultâneas compartilham uma única renovação', async () => {
    tokenStore.set(session('velho'));
    let refreshes = 0;
    vi.stubGlobal('fetch', vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      if (String(url).endsWith('/auth/refresh')) {
        refreshes++;
        return json(200, session('novo'));
      }
      return ((init?.headers ?? {}) as Record<string, string>).Authorization === 'Bearer novo' ? json(200, {}) : json(401, {});
    }));

    await Promise.all([api('/a'), api('/b'), api('/c')]);
    expect(refreshes).toBe(1);
  });

  it('quando a renovação falha, encerra a sessão local e devolve 401', async () => {
    tokenStore.set(session('velho'));
    const expired = vi.fn();
    tokenStore.onExpired(expired);
    vi.stubGlobal('fetch', vi.fn(async () => json(401, { title: 'Sessão inválida' })));

    await expect(api('/tickets')).rejects.toMatchObject({ status: 401 });
    expect(expired).toHaveBeenCalledOnce();
    expect(tokenStore.get()).toBeNull();
  });

  it('não tenta renovar a sessão em chamadas sem autenticação (login)', async () => {
    const fetchMock = vi.fn(async () => json(401, { title: 'Credenciais inválidas' }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(api('/auth/login', { method: 'POST', body: {}, auth: false })).rejects.toBeInstanceOf(ApiError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('converte Problem Details em ApiError com erros por campo', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(400, { title: 'Erros de validação', errors: { Title: ['Título obrigatório.'] } })));

    const error = await api('/tickets', { method: 'POST', body: {} }).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(messageOf(error)).toBe('Título obrigatório.');
  });

  it('usa mensagem genérica para erros desconhecidos', () => {
    expect(messageOf(new Error('boom'))).toContain('Não foi possível');
  });

  it('refreshSession devolve nulo e limpa o token quando o cookie não é válido', async () => {
    tokenStore.set(session('x'));
    vi.stubGlobal('fetch', vi.fn(async () => json(401, {})));
    expect(await refreshSession()).toBeNull();
    expect(tokenStore.get()).toBeNull();
  });
});
