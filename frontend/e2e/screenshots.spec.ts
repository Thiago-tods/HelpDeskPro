import path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { firstTicketId, openSession } from './support';

// Gera as imagens do README. Só roda com CAPTURE_SCREENSHOTS=1 (ver docs/DEVELOPMENT.md):
//   $env:CAPTURE_SCREENSHOTS = '1'; .\scripts\e2e.ps1 screenshots
test.skip(!process.env.CAPTURE_SCREENSHOTS, 'Somente para gerar screenshots da documentação.');

const OUT = path.resolve(__dirname, '..', '..', 'docs', 'screenshots');
const DESKTOP = { width: 1440, height: 900 };

async function ready(page: Page) {
  await expect(page.locator('main h1').first()).toBeVisible();
  await expect.poll(() => page.locator('.skeleton, [role="status"][aria-label^="Carregando"]').count(), { timeout: 15_000 }).toBe(0);
  await page.waitForTimeout(1800); // animação dos gráficos
}

async function shot(page: Page, name: string, fullPage = false) {
  await page.screenshot({ path: path.join(OUT, `${name}.png`), fullPage });
}

test('capturas desktop', async ({ browser }) => {
  // Login (tema claro)
  const anon = await browser.newContext({ viewport: DESKTOP, locale: 'pt-BR', reducedMotion: 'reduce' });
  await anon.addInitScript(() => localStorage.setItem('hdp-theme', 'light'));
  const login = await anon.newPage();
  await login.goto('/login');
  await expect(login.getByRole('heading', { name: 'Entrar' })).toBeVisible();
  await shot(login, '01-login');
  await anon.close();

  // Administrador — painel (claro) e relatórios
  const admin = await openSession(browser, 'admin', { theme: 'light', viewport: DESKTOP });
  await ready(admin.page);
  await shot(admin.page, '02-painel-admin-claro');
  await admin.page.goto('/admin/reports');
  await ready(admin.page);
  await shot(admin.page, '07-relatorios');
  await admin.page.goto('/admin/users');
  await ready(admin.page);
  await shot(admin.page, '08-usuarios');
  await admin.context.close();

  // Agente — painel (escuro), kanban, detalhe
  const agent = await openSession(browser, 'camila', { theme: 'dark', viewport: DESKTOP });
  await ready(agent.page);
  await shot(agent.page, '03-painel-agente-escuro');
  await agent.page.goto('/kanban');
  await ready(agent.page);
  await shot(agent.page, '04-kanban-escuro');
  const detailId = await firstTicketId('camila');
  await agent.page.goto(`/tickets/${detailId}`);
  await ready(agent.page);
  await shot(agent.page, '05-detalhe-chamado-escuro');
  await agent.context.close();

  // Solicitante — início e abertura de chamado (claro)
  const requester = await openSession(browser, 'ana', { theme: 'light', viewport: DESKTOP });
  await ready(requester.page);
  await shot(requester.page, '06-inicio-solicitante-claro');
  await requester.page.goto('/tickets');
  await ready(requester.page);
  await shot(requester.page, '09-meus-chamados');
  await requester.context.close();
});

test('captura mobile', async ({ browser }) => {
  const s = await openSession(browser, 'ana', { theme: 'light', viewport: { width: 390, height: 844 } });
  await ready(s.page);
  await shot(s.page, '10-mobile-inicio');
  await s.page.getByRole('button', { name: 'Abrir menu de navegação' }).click();
  await s.page.waitForTimeout(400);
  await shot(s.page, '11-mobile-menu');
  await s.context.close();
});
