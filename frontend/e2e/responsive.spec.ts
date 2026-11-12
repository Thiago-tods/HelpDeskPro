import { expect, test } from '@playwright/test';
import { firstTicketId, openSession, type Persona } from './support';

const VIEWPORTS = [
  { name: 'celular', width: 375, height: 812 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'desktop', width: 1366, height: 800 },
];

const PAGES: { persona: Persona; paths: string[] }[] = [
  { persona: 'ana', paths: ['/', '/tickets', '/tickets/new', '/notifications', '/profile'] },
  { persona: 'camila', paths: ['/', '/queue', '/kanban'] },
  { persona: 'admin', paths: ['/', '/admin/users', '/admin/teams', '/admin/priorities', '/admin/reports', '/admin/audit', '/admin/settings'] },
];

for (const vp of VIEWPORTS) {
  test.describe(`layout responsivo — ${vp.name} (${vp.width}px)`, () => {
    for (const { persona, paths } of PAGES) {
      test(`${persona}: sem rolagem horizontal da página`, async ({ browser }) => {
        const { page, context } = await openSession(browser, persona, { viewport: { width: vp.width, height: vp.height } });
        const all = persona === 'ana' || persona === 'camila' ? [...paths, `/tickets/${await firstTicketId(persona)}`] : paths;
        for (const path of all) {
          await page.goto(path);
          await expect(page.locator('main h1').first()).toBeVisible();
          await page.waitForTimeout(300);
          const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
          expect(overflow, `rolagem horizontal em ${path}`).toBeLessThanOrEqual(1);
        }
        await context.close();
      });
    }
  });
}

test('celular: o menu vira gaveta, abre pelo botão e fecha ao navegar', async ({ browser }) => {
  const { page, context } = await openSession(browser, 'ana', { viewport: { width: 375, height: 812 } });
  await expect(page.getByRole('navigation', { name: 'Menu principal' })).toBeHidden();

  await page.getByRole('button', { name: 'Abrir menu de navegação' }).click();
  const nav = page.getByRole('dialog').getByRole('navigation', { name: 'Menu principal' });
  await expect(nav).toBeVisible();

  await nav.getByRole('link', { name: 'Meus chamados' }).click();
  await expect(page).toHaveURL(/\/tickets$/);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await context.close();
});

test('desktop: a barra lateral fica fixa e visível', async ({ browser }) => {
  const { page, context } = await openSession(browser, 'admin', { viewport: { width: 1366, height: 800 } });
  await expect(page.getByRole('navigation', { name: 'Menu principal' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Abrir menu de navegação' })).toBeHidden();
  await context.close();
});
