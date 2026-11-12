import { expect, request, test } from '@playwright/test';
import { API, PASSWORD, USERS, apiAs, loginViaUi, openSession, openTicketViaUi, uniqueTitle } from './support';

test.describe('isolamento de dados (IDOR)', () => {
  test('solicitante não acessa chamado de outro solicitante, nem pela interface nem pela API', async ({ browser }) => {
    const ana = await openSession(browser, 'ana');
    const { id } = await openTicketViaUi(ana.page, uniqueTitle('E2E privado'));

    const carlos = await openSession(browser, 'carlos');
    await carlos.page.goto(`/tickets/${id}`);
    await expect(carlos.page.getByText('Chamado não encontrado')).toBeVisible();
    await expect(carlos.page.getByText(/E2E privado/)).toHaveCount(0);

    // Mesmo apelando direto para a API com o token dele: 404 (não revela existência).
    const { api } = await apiAs('carlos');
    for (const suffix of ['', '/messages', '/history', '/attachments']) {
      expect((await api.get(`${API}/tickets/${id}${suffix}`)).status(), `GET ${suffix}`).toBe(404);
    }
    expect((await api.patch(`${API}/tickets/${id}/status`, { data: { status: 'CANCELLED' } })).status()).toBe(404);
    await api.dispose();
    for (const s of [ana, carlos]) await s.context.close();
  });

  test('solicitante nunca vê notas internas, nem pela interface nem pela API', async ({ browser }) => {
    const note = `SEGREDO-${Date.now()}`;
    const ana = await openSession(browser, 'ana');
    const { id } = await openTicketViaUi(ana.page, uniqueTitle('E2E nota'));

    const camila = await apiAs('camila');
    expect((await camila.api.patch(`${API}/tickets/${id}/assignment`, { data: { assignedToId: await userId('camila') } })).ok()).toBeTruthy();
    expect((await camila.api.post(`${API}/tickets/${id}/messages`, { data: { content: note, isInternal: true } })).status()).toBe(201);
    await camila.api.dispose();

    await ana.page.reload();
    await ana.page.getByRole('tab', { name: /Conversa/ }).click();
    await expect(ana.page.getByText(note)).toHaveCount(0);

    const anaApi = await apiAs('ana');
    const messages = await (await anaApi.api.get(`${API}/tickets/${id}/messages`)).text();
    expect(messages).not.toContain(note);
    // E tampouco consegue criar uma nota interna.
    expect((await anaApi.api.post(`${API}/tickets/${id}/messages`, { data: { content: 'tentativa', isInternal: true } })).status()).toBe(403);
    await anaApi.api.dispose();
    await ana.context.close();
  });

  test('agente de outra equipe não vê nem acessa o chamado', async ({ browser }) => {
    const title = uniqueTitle('E2E equipe N1');
    const ana = await openSession(browser, 'ana');
    const { id } = await openTicketViaUi(ana.page, title, { category: 'Hardware' }); // equipe Suporte N1

    const bruno = await openSession(browser, 'bruno'); // equipe Infraestrutura
    await bruno.page.goto(`/tickets/${id}`);
    await expect(bruno.page.getByText('Chamado não encontrado')).toBeVisible();

    await bruno.page.goto(`/tickets?q=${encodeURIComponent(title)}`);
    await expect(bruno.page.getByText('Nenhum chamado encontrado')).toBeVisible();

    const { api } = await apiAs('bruno');
    expect((await api.patch(`${API}/tickets/${id}/assignment`, { data: { assignedToId: await userId('bruno') } })).status()).toBe(404);
    await api.dispose();

    // O agente da equipe certa enxerga normalmente.
    const camila = await openSession(browser, 'camila');
    await camila.page.goto(`/tickets/${id}`);
    await expect(camila.page.getByRole('heading', { name: title })).toBeVisible();
    for (const s of [ana, bruno, camila]) await s.context.close();
  });
});

test.describe('permissões por papel', () => {
  for (const [persona, route] of [['ana', '/admin/users'], ['camila', '/admin/users'], ['camila', '/admin/audit'], ['ana', '/kanban'], ['ana', '/queue']] as const) {
    test(`${persona} em ${route} vê 403 e o menu não oferece o caminho`, async ({ browser }) => {
      const s = await openSession(browser, persona);
      await s.page.goto(route);
      await expect(s.page.getByRole('heading', { name: 'Acesso negado' })).toBeVisible();
      const nav = s.page.getByRole('navigation', { name: 'Menu principal' });
      await expect(nav.locator(`a[href="${route}"]`)).toHaveCount(0);
      await s.context.close();
    });
  }
});

test.describe('contas e sessões', () => {
  test('usuário desativado não consegue autenticar', async ({ browser }) => {
    const email = `e2e.desativado.${Date.now().toString(36)}@nexora.example`;
    const initial = `Tmp!${Date.now()}aZ`;
    const admin = await openSession(browser, 'admin');

    // O administrador cria o usuário pela interface…
    await admin.page.goto('/admin/users');
    await admin.page.getByRole('button', { name: 'Novo usuário' }).click();
    const modal = admin.page.getByRole('dialog', { name: 'Novo usuário' });
    await modal.getByLabel('Nome completo').fill('Pessoa Desativada E2E');
    await modal.getByLabel('E-mail').fill(email);
    await modal.getByLabel('Senha inicial').fill(initial);
    await modal.getByRole('button', { name: 'Criar usuário' }).click();
    await expect(admin.page.getByText('Pessoa Desativada E2E')).toBeVisible();

    // …que consegue entrar…
    const user = await browser.newContext();
    const userPage = await user.newPage();
    await loginViaUi(userPage, email, initial);
    await user.clearCookies();
    await userPage.close();

    // …até ser desativado (com confirmação).
    await admin.page.getByRole('button', { name: 'Desativar Pessoa Desativada E2E' }).click();
    await admin.page.getByRole('dialog').getByRole('button', { name: 'Desativar' }).click();
    await expect(admin.page.getByText('Usuário desativado.')).toBeVisible();

    const retry = await user.newPage();
    await retry.goto('/login');
    await retry.getByLabel('E-mail').fill(email);
    await retry.getByLabel('Senha').fill(initial);
    await retry.getByRole('button', { name: 'Entrar' }).click();
    await expect(retry.getByRole('alert')).toContainText('E-mail ou senha incorretos');
    await expect(retry.getByRole('navigation', { name: 'Menu principal' })).toHaveCount(0);
    await admin.context.close();
    await user.close();
  });

  test('refresh token revogado (logout) não renova a sessão', async ({ browser }) => {
    const s = await openSession(browser, 'ana');
    const stolen = (await s.context.cookies()).find((c) => c.name === 'hdp_refresh');
    expect(stolen, 'cookie de refresh presente').toBeTruthy();
    expect(stolen!.httpOnly).toBe(true);
    // O JavaScript da página não enxerga o cookie.
    expect(await s.page.evaluate(() => document.cookie)).not.toContain('hdp_refresh');

    // Logout pela interface.
    await s.page.getByRole('button', { name: /Menu do usuário/ }).click();
    await s.page.getByRole('menuitem', { name: 'Sair' }).click();
    await expect(s.page.getByRole('heading', { name: 'Entrar' })).toBeVisible();

    // O valor antigo do cookie (como se tivesse sido copiado) já não renova nada.
    const attacker = await request.newContext({ extraHTTPHeaders: { Cookie: `hdp_refresh=${stolen!.value}`, 'X-Requested-With': 'XMLHttpRequest' } });
    expect((await attacker.post(`${API}/auth/refresh`)).status()).toBe(401);
    await attacker.dispose();

    // E recarregar a página não restaura a sessão.
    await s.page.goto('/');
    await expect(s.page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
    await s.context.close();
  });

  test('encerrar todas as sessões derruba os outros dispositivos', async ({ browser }) => {
    const a = await openSession(browser, 'carlos');
    const b = await openSession(browser, 'carlos'); // "outro dispositivo"

    await a.page.goto('/profile');
    await a.page.getByRole('button', { name: 'Encerrar todas as sessões' }).click();
    await a.page.getByRole('dialog').getByRole('button', { name: 'Encerrar sessões' }).click();
    await expect(a.page.getByRole('heading', { name: 'Entrar' })).toBeVisible();

    await b.page.reload();
    await expect(b.page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
    for (const s of [a, b]) await s.context.close();
  });

  test('credenciais inválidas: mensagem genérica, sem revelar se o e-mail existe', async ({ page }) => {
    const message = async (email: string) => {
      await page.goto('/login');
      await page.getByLabel('E-mail').fill(email);
      await page.getByLabel('Senha').fill('SenhaErrada!123');
      await page.getByRole('button', { name: 'Entrar' }).click();
      return (await page.getByRole('alert').textContent()) ?? '';
    };
    const known = await message(USERS.ana);
    const unknown = await message('ninguem@nexora.example');
    expect(known).toBe(unknown);
    expect(PASSWORD).toBeTruthy();
  });
});

test.describe('XSS e conteúdo hostil', () => {
  test('HTML/script em título e mensagens é exibido como texto, nunca executado', async ({ browser }) => {
    const payload = `<img src=x onerror="window.__xss=1"><script>window.__xss=2</script> ${Date.now()}`;
    const ana = await openSession(browser, 'ana');
    const dialogs: string[] = [];
    ana.page.on('dialog', (d) => { dialogs.push(d.message()); void d.dismiss(); });

    const { id } = await openTicketViaUi(ana.page, payload, { description: `<b onmouseover="window.__xss=3">negrito?</b> javascript:alert(1)` });
    await expect(ana.page.getByRole('heading', { name: payload })).toBeVisible(); // texto literal
    await ana.page.getByLabel('Nova mensagem').fill('<svg onload="window.__xss=4"> mensagem');
    await ana.page.getByRole('button', { name: 'Enviar' }).click();
    await expect(ana.page.getByText('<svg onload="window.__xss=4"> mensagem')).toBeVisible();

    await ana.page.goto('/tickets');
    await expect(ana.page.getByRole('link', { name: new RegExp(payload.slice(0, 20).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')) }).first()).toBeVisible();

    expect(await ana.page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
    expect(await ana.page.locator('main img[src="x"], main script').count()).toBe(0);
    expect(dialogs).toEqual([]);
    expect(id).toBeTruthy();
    await ana.context.close();
  });
});

async function userId(persona: keyof typeof USERS): Promise<string> {
  const { api } = await apiAs('admin');
  const users = (await (await api.get(`${API}/users`)).json()) as { id: string; email: string }[];
  await api.dispose();
  return users.find((u) => u.email === USERS[persona])!.id;
}
