import type { AuthSession } from '@/types/api';

// Em desenvolvimento usa o proxy do Vite (same-origin). VITE_API_URL permite apontar direto para a API (exige CORS).
const BASE = (import.meta.env.VITE_API_URL as string | undefined) || '/api/v1';

export const API_ORIGIN = BASE.startsWith('http') ? new URL(BASE).origin : '';

interface TokenState {
  token: string | null;
  expiresAt: number;
  onExpired: (() => void) | null;
}

// O access token vive apenas em memória (nunca em localStorage). O refresh token fica em cookie HttpOnly.
const state: TokenState = { token: null, expiresAt: 0, onExpired: null };

export const tokenStore = {
  get: () => state.token,
  set(next: { accessToken: string; expiresAt: string } | null) {
    state.token = next?.accessToken ?? null;
    state.expiresAt = next ? new Date(next.expiresAt).getTime() : 0;
  },
  isExpiringSoon: () => state.token !== null && Date.now() > state.expiresAt - 30_000,
  onExpired(callback: (() => void) | null) {
    state.onExpired = callback;
  },
};

export class ApiError extends Error {
  status: number;
  title: string;
  detail?: string;
  errors?: Record<string, string[]>;

  constructor(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
    super(detail ?? title);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.errors = errors;
  }
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  query?: Record<string, string | number | boolean | undefined | null>;
  signal?: AbortSignal;
  /** false: não anexa token nem tenta renovar a sessão (login, refresh). */
  auth?: boolean;
}

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const url = `${BASE}${path}`;
  if (!query) return url;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '' || value === false) continue;
    params.set(key, String(value));
  }
  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

function toApiError(status: number, data: unknown): ApiError {
  const problem = (data ?? {}) as { title?: string; detail?: string; errors?: Record<string, string[]> };
  return new ApiError(status, problem.title ?? 'Erro na requisição', problem.detail, problem.errors);
}

async function rawFetch(path: string, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = { 'X-Requested-With': 'XMLHttpRequest' };
  let body: BodyInit | undefined;
  if (options.body instanceof FormData) {
    body = options.body;
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }
  if (options.auth !== false && state.token) headers.Authorization = `Bearer ${state.token}`;

  return fetch(buildUrl(path, options.query), {
    method: options.method ?? 'GET',
    headers,
    body,
    signal: options.signal,
    credentials: 'include',
  });
}

let refreshing: Promise<AuthSession | null> | null = null;

/** Renova a sessão usando o cookie HttpOnly. Chamadas simultâneas compartilham a mesma requisição. */
export function refreshSession(): Promise<AuthSession | null> {
  refreshing ??= (async () => {
    try {
      const res = await rawFetch('/auth/refresh', { method: 'POST', auth: false });
      if (!res.ok) {
        tokenStore.set(null);
        return null;
      }
      const session = (await res.json()) as AuthSession;
      tokenStore.set(session);
      return session;
    } catch {
      tokenStore.set(null);
      return null;
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

/** Envia a requisição; em 401 tenta renovar a sessão uma vez e repete. */
export async function send(path: string, options: RequestOptions = {}): Promise<Response> {
  if (options.auth !== false && tokenStore.isExpiringSoon()) await refreshSession();

  let res = await rawFetch(path, options);
  if (res.status === 401 && options.auth !== false) {
    const renewed = await refreshSession();
    if (!renewed) {
      state.onExpired?.();
      return res;
    }
    res = await rawFetch(path, options);
  }
  return res;
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const res = await send(path, options);
  const text = await res.text();
  let data: unknown;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = undefined;
    }
  }
  if (!res.ok) throw toApiError(res.status, data);
  return data as T;
}

/** Baixa um arquivo autenticado e dispara o download no navegador. */
export async function downloadFile(path: string, fallbackName: string, query?: RequestOptions['query']): Promise<void> {
  const res = await send(path, { query });
  if (!res.ok) throw toApiError(res.status, undefined);
  const blob = await res.blob();
  const disposition = res.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;

  const href = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = href;
  anchor.download = name;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(href);
}

export function messageOf(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.errors) {
      const first = Object.values(error.errors).flat()[0];
      if (first) return first;
    }
    return error.detail ?? error.title;
  }
  return 'Não foi possível concluir a operação. Tente novamente.';
}
