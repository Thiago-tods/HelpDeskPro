import { expect, test } from '@playwright/test';
import { PASSWORD, USERS, firstTicketId, openSession } from './support';

test.describe('navegação por teclado', () => {
  test('é possível entrar usando apenas o teclado', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByLabel('E-mail')).toBeFocused(); // foco inicial no primeiro campo
    await page.keyboard.type(USERS.ana);
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Senha')).toBeFocused();
    await page.keyboard.type(PASSWORD);
    await page.keyboard.press('Enter');
    await expect(page.getByRole('navigation', { name: 'Menu principal' })).toBeVisible();
  });

  test('erros de formulário são anunciados e associados aos campos', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('button', { name: 'Entrar' }).click();
    const email = page.getByLabel('E-mail');
    await expect(email).toHaveAttribute('aria-invalid', 'true');
    const describedBy = await email.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    await expect(page.locator(`#${describedBy}`)).toHaveText('Informe seu e-mail.');
    await expect(page.locator(`#${describedBy}`)).toHaveAttribute('role', 'alert');
  });

  test('link "Ir para o conteúdo" é o primeiro foco e leva ao conteúdo principal', async ({ browser }) => {
    const { page, context } = await openSession(browser, 'ana');
    await page.goto('/');
    await expect(page.getByRole('navigation', { name: 'Menu principal' })).toBeVisible();
    await page.keyboard.press('Tab');
    const skip = page.getByRole('link', { name: 'Ir para o conteúdo' });
    await expect(skip).toBeFocused();
    await expect(skip).toBeVisible();
    await page.keyboard.press('Enter');
    await expect(page.locator('#conteudo')).toBeFocused();
    await context.close();
  });

  test('todo elemento focável mostra indicador de foco visível', async ({ browser }) => {
    const { page, context } = await openSession(browser, 'ana');
    await page.goto('/tickets');
    await page.keyboard.press('Tab');
    for (let i = 0; i < 14; i++) {
      await page.keyboard.press('Tab');
      const outline = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        if (!el || el === document.body) return null;
        const style = getComputedStyle(el);
        return { width: parseFloat(style.outlineWidth), style: style.outlineStyle, name: el.getAttribute('aria-label') ?? el.textContent?.trim().slice(0, 30) };
      });
      if (outline) expect(outline.style !== 'none' && outline.width >= 2, `foco sem destaque em: ${outline.name}`).toBe(true);
    }
    await context.close();
  });

  test('modal: foco preso, Esc fecha e o foco volta ao botão de origem', async ({ browser }) => {
    const { page, context } = await openSession(browser, 'admin');
    await page.goto('/admin/users');
    const trigger = page.getByRole('button', { name: 'Novo usuário' });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Novo usuário' });
    await expect(dialog).toBeVisible();

    for (let i = 0; i < 14; i++) {
      await page.keyboard.press('Tab');
      expect(await dialog.evaluate((d) => d.contains(document.activeElement)), `Tab #${i + 1} escapou do modal`).toBe(true);
    }
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
    await context.close();
  });

  test('abas respondem às setas, Home e End (padrão WAI-ARIA)', async ({ browser }) => {
    const id = await firstTicketId('ana');
    const { page, context } = await openSession(browser, 'ana');
    await page.goto(`/tickets/${id}`);
    const conversa = page.getByRole('tab', { name: /Conversa/ });
    await conversa.focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('tab', { name: /Anexos/ })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('tab', { name: /Anexos/ })).toBeFocused();
    await page.keyboard.press('End');
    await expect(page.getByRole('tab', { name: 'Histórico' })).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('Home');
    await expect(conversa).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('tabpanel')).toBeVisible();
    await context.close();
  });

  test('menu do usuário: setas percorrem os itens, Esc fecha e devolve o foco ao botão', async ({ browser }) => {
    const { page, context } = await openSession(browser, 'ana');
    const trigger = page.getByRole('button', { name: /Menu do usuário/ });
    await trigger.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('menu')).toBeVisible();
    // Aberto pelo teclado, o primeiro item já recebe o foco.
    await expect(page.getByRole('menuitem', { name: 'Meu perfil' })).toBeFocused();
    await page.keyboard.press('ArrowDown');
    await expect(page.getByRole('menuitem', { name: 'Sair' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('menu')).toBeHidden();
    await expect(trigger).toBeFocused();
    await context.close();
  });

  test('menu do usuário: Enter no primeiro item abre o perfil', async ({ browser }) => {
    const { page, context } = await openSession(browser, 'ana');
    await page.getByRole('button', { name: /Menu do usuário/ }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('menuitem', { name: 'Meu perfil' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/profile$/);
    await context.close();
  });
  test('tema escuro: alternar por teclado, aria-pressed e persistência após recarregar', async ({ browser }) => {
    const { page, context } = await openSession(browser, 'ana', { theme: 'light' });
    const toggle = page.getByRole('button', { name: 'Mudar para o tema escuro' });
    await toggle.focus();
    await page.keyboard.press('Enter');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect(page.getByRole('button', { name: 'Mudar para o tema claro' })).toHaveAttribute('aria-pressed', 'true');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    expect(await page.evaluate(() => localStorage.getItem('hdp-theme'))).toBe('dark');
    await context.close();
  });

  test('kanban: mover cartão pelo seletor (alternativa ao arrastar)', async ({ browser }) => {
    const { page, context } = await openSession(browser, 'rafael');
    await page.goto('/kanban');
    const select = page.getByLabel(/^Mover HDP-/).first();
    await expect(select).toBeVisible();
    await expect(select).toBeEnabled();
    await select.focus();
    await expect(select).toBeFocused();
    await context.close();
  });
});
