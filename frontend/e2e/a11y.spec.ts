import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { firstTicketId, openSession } from './support';

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/**
 * Espera a tela estabilizar: título visível e sem esqueletos de carregamento.
 * Os cenários rodam com reducedMotion (o app respeita prefers-reduced-motion e desliga animações); sem isso o axe
 * mediria o contraste durante a animação de entrada, com opacidade parcial.
 */
async function settle(page: Page) {
  await expect(page.locator('main h1').first()).toBeVisible();
  await expect.poll(() => page.locator('.skeleton, [role="status"][aria-label^="Carregando"]').count(), { timeout: 15_000 }).toBe(0);
  await page.waitForTimeout(350);
}

/** Executa o axe e devolve um resumo legível das violações (vazio = aprovado). */
async function violationsOf(page: Page): Promise<string[]> {
  const results = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return results.violations.map(
    (v) => `${v.id} [${v.impact}] ${v.help} — ${v.nodes.length} nó(s): ${v.nodes.slice(0, 3).map((n) => n.target.join(' ')).join(' | ')}`,
  );
}

async function scan(page: Page, label: string, path?: string) {
  if (path) await page.goto(path);
  await settle(page);
  expect.soft(await violationsOf(page), `Acessibilidade em ${label}`).toEqual([]);
}

for (const theme of ['light', 'dark'] as const) {
  test.describe(`acessibilidade — tema ${theme}`, () => {
    test('visitante: login, recuperação e redefinição', async ({ browser }) => {
      const context = await browser.newContext({ locale: 'pt-BR', reducedMotion: 'reduce' });
      await context.addInitScript((t) => localStorage.setItem('hdp-theme', t), theme);
      const page = await context.newPage();
      for (const [path, heading] of [['/login', 'Entrar'], ['/forgot-password', 'Recuperar senha'], ['/reset-password', 'Link inválido']] as const) {
        await page.goto(path);
        await expect(page.getByRole('heading', { name: heading })).toBeVisible();
        expect.soft(await violationsOf(page), `Acessibilidade em ${path}`).toEqual([]);
      }
      // Estado de erro de validação também precisa ser acessível.
      await page.goto('/login');
      await page.getByRole('button', { name: 'Entrar' }).click();
      await expect(page.getByText('Informe seu e-mail.')).toBeVisible();
      expect.soft(await violationsOf(page), 'Acessibilidade em /login com erros').toEqual([]);
      await context.close();
    });

    test('solicitante: telas e menus', async ({ browser }) => {
      const detailId = await firstTicketId('ana');
      const { page, context } = await openSession(browser, 'ana', { theme, reducedMotion: true });
      await scan(page, 'início do solicitante', '/');
      await scan(page, 'meus chamados', '/tickets');
      await scan(page, 'abrir chamado', '/tickets/new');
      await scan(page, 'detalhe do chamado', `/tickets/${detailId}`);
      for (const tab of ['Anexos', 'Histórico']) {
        await page.getByRole('tab', { name: new RegExp(tab) }).click();
        await page.waitForTimeout(250);
        expect.soft(await violationsOf(page), `Aba ${tab}`).toEqual([]);
      }
      await scan(page, 'notificações', '/notifications');
      await scan(page, 'perfil', '/profile');

      await page.goto('/');
      await settle(page);
      await page.getByRole('button', { name: /^Notificações/ }).click();
      await expect(page.getByRole('menu')).toBeVisible();
      expect.soft(await violationsOf(page), 'menu de notificações aberto').toEqual([]);
      await page.keyboard.press('Escape');
      await page.getByRole('button', { name: /Menu do usuário/ }).click();
      await expect(page.getByRole('menu')).toBeVisible();
      expect.soft(await violationsOf(page), 'menu do usuário aberto').toEqual([]);
      await context.close();
    });

    test('agente: painel, fila, kanban e detalhe', async ({ browser }) => {
      const detailId = await firstTicketId('camila');
      const { page, context } = await openSession(browser, 'camila', { theme, reducedMotion: true });
      await scan(page, 'painel de atendimento', '/');
      await scan(page, 'fila', '/queue');
      await scan(page, 'atribuídos', '/assigned');
      await scan(page, 'kanban', '/kanban');
      await scan(page, 'todos os chamados', '/tickets');
      await scan(page, 'detalhe (agente)', `/tickets/${detailId}`);
      await context.close();
    });

    test('administrador: painel e todas as telas administrativas', async ({ browser }) => {
      const { page, context } = await openSession(browser, 'admin', { theme, reducedMotion: true });
      await scan(page, 'visão geral', '/');
      await scan(page, 'usuários', '/admin/users');
      await page.getByRole('button', { name: 'Novo usuário' }).click();
      await expect(page.getByRole('dialog', { name: 'Novo usuário' })).toBeVisible();
      expect.soft(await violationsOf(page), 'modal Novo usuário').toEqual([]);
      await page.keyboard.press('Escape');

      await scan(page, 'equipes', '/admin/teams');
      await page.getByRole('button', { name: /^Membros de/ }).first().click();
      await expect(page.getByRole('dialog')).toBeVisible();
      expect.soft(await violationsOf(page), 'modal de membros').toEqual([]);
      await page.keyboard.press('Escape');

      await scan(page, 'categorias', '/admin/categories');
      await scan(page, 'prioridades', '/admin/priorities');
      await page.getByRole('button', { name: /^Editar / }).first().click();
      await expect(page.getByRole('dialog')).toBeVisible();
      expect.soft(await violationsOf(page), 'modal de prioridade').toEqual([]);
      await page.keyboard.press('Escape');

      await scan(page, 'SLA', '/admin/sla');
      await scan(page, 'relatórios', '/admin/reports');
      await scan(page, 'auditoria', '/admin/audit');
      await scan(page, 'configurações', '/admin/settings');
      await scan(page, 'página 403/404', '/rota-inexistente');
      await context.close();
    });
  });
}
