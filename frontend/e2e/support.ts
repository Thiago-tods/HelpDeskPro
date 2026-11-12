import { expect, request, type APIRequestContext, type Browser, type BrowserContext, type Page } from '@playwright/test';

export const API = 'http://localhost:5090/api/v1';
export const PASSWORD = process.env.E2E_PASSWORD!;

export const USERS = {
  admin: 'admin@nexora.example',
  rafael: 'rafael.mendes@nexora.example', // Suporte N1 + Infraestrutura
  camila: 'camila.torres@nexora.example', // Suporte N1
  bruno: 'bruno.figueira@nexora.example', // Infraestrutura
  ana: 'ana.lima@nexora.example',
  carlos: 'carlos.souza@nexora.example',
} as const;

export type Persona = keyof typeof USERS;

export interface Session {
  context: BrowserContext;
  page: Page;
}

/** Faz login pela interface (o fluxo real do usuário) e espera o painel carregar. */
export async function loginViaUi(page: Page, email: string, password = PASSWORD): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(password);
  await page.getByRole('button', { name: 'Entrar' }).click();
  // O menu lateral é uma gaveta em telas pequenas; o conteúdo principal existe em qualquer largura.
  await expect(page.locator('#conteudo')).toBeVisible();
}

/** Abre um contexto de navegador isolado (cookies próprios) já autenticado. */
export async function openSession(browser: Browser, persona: Persona, options: { theme?: 'light' | 'dark'; viewport?: { width: number; height: number }; reducedMotion?: boolean } = {}): Promise<Session> {
  const context = await browser.newContext({ locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', viewport: options.viewport, reducedMotion: options.reducedMotion ? 'reduce' : undefined });
  if (options.theme) {
    // Define o tema inicial só quando ainda não há preferência salva (senão um reload desfaria a escolha do usuário).
    await context.addInitScript((theme) => { if (!localStorage.getItem('hdp-theme')) localStorage.setItem('hdp-theme', theme); }, options.theme);
  }
  const page = await context.newPage();
  await loginViaUi(page, USERS[persona]);
  return { context, page };
}

/** Cliente de API autenticado (para preparar/inspecionar dados fora da interface). */
export async function apiAs(persona: Persona): Promise<{ api: APIRequestContext; token: string }> {
  const anonymous = await request.newContext();
  const res = await anonymous.post(`${API}/auth/login`, { data: { email: USERS[persona], password: PASSWORD } });
  expect(res.ok()).toBeTruthy();
  const { accessToken } = (await res.json()) as { accessToken: string };
  await anonymous.dispose();
  const api = await request.newContext({ extraHTTPHeaders: { Authorization: `Bearer ${accessToken}`, 'X-Requested-With': 'XMLHttpRequest' } });
  return { api, token: accessToken };
}

export async function firstTicketId(persona: Persona, query = ''): Promise<string> {
  const { api } = await apiAs(persona);
  const res = await api.get(`${API}/tickets?pageSize=1${query}`);
  const body = (await res.json()) as { items: { id: string }[] };
  await api.dispose();
  if (!body.items[0]) throw new Error('Nenhum chamado encontrado para o cenário.');
  return body.items[0].id;
}

export const uniqueTitle = (prefix: string) => `${prefix} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

/** Abre um chamado pela tela, como o solicitante faria. Devolve número e URL do detalhe. */
export async function openTicketViaUi(page: Page, title: string, options: { category?: string; priority?: string; description?: string } = {}) {
  await page.goto('/tickets/new');
  await page.getByLabel('Assunto').fill(title);
  await page.getByLabel('Categoria').selectOption({ label: options.category ?? 'Hardware' });
  await page.getByLabel('Prioridade').selectOption({ label: options.priority ?? 'Média' });
  await page.getByLabel('Descrição').fill(options.description ?? 'Descrição detalhada criada pelo teste automatizado.');
  await page.getByRole('button', { name: 'Abrir chamado' }).click();
  await page.waitForURL(/\/tickets\/[0-9a-f-]{36}$/);
  const url = page.url();
  const number = (await page.getByText(/HDP-\d{4}-\d{6}/).first().textContent())?.match(/HDP-\d{4}-\d{6}/)?.[0] ?? '';
  return { url, number, id: url.split('/').pop()! };
}
