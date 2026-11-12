import { expect, test } from '@playwright/test';
import { apiAs, openSession, openTicketViaUi, uniqueTitle, API } from './support';

/**
 * Cenário obrigatório (docs/TESTING.md): três pessoas, três navegadores isolados, atualizações em tempo real.
 *  1. Solicitante autentica  2. Abre chamado  3. Agente recebe a atualização  4. Agente assume
 *  5. Agente responde publicamente  6. Solicitante recebe a resposta  7. Agente resolve
 *  8. Solicitante acompanha  9. Administrador consulta indicadores
 */
test('fluxo completo: abertura, atendimento, resolução e indicadores', async ({ browser }) => {
  const title = uniqueTitle('E2E fluxo');
  const publicReply = `Olá Ana, estamos verificando o seu caso. (${Date.now()})`;
  const internalNote = `NOTA-INTERNA-SIGILOSA-${Date.now()}`;

  // Linha de base dos indicadores do administrador (via API).
  const adminApi = await apiAs('admin');
  const before = (await (await adminApi.api.get(`${API}/dashboard/summary?days=30`)).json()) as { total: number; resolved: number; closed: number };

  // 1) Solicitante autentica
  const ana = await openSession(browser, 'ana');
  await expect(ana.page.getByRole('heading', { name: /Olá, Ana/ })).toBeVisible();

  // Agente já está logado, olhando a fila (será atualizado em tempo real)
  const camila = await openSession(browser, 'camila');
  await camila.page.goto('/queue');
  await expect(camila.page.getByRole('heading', { name: 'Fila de atendimento' })).toBeVisible();
  await expect(camila.page.getByText('Ao vivo')).toBeVisible();
  await expect(camila.page.getByRole('link', { name: new RegExp(title) })).toHaveCount(0);

  // 2) Solicitante abre o chamado
  const ticket = await openTicketViaUi(ana.page, title, { category: 'Hardware', priority: 'Média' });
  await expect(ana.page.getByRole('heading', { name: title })).toBeVisible();
  await expect(ana.page.getByText('Novo', { exact: true }).first()).toBeVisible();
  expect(ticket.number).toMatch(/^HDP-\d{4}-\d{6}$/);

  // 3) Agente recebe a atualização sem recarregar a página
  const queueRow = camila.page.getByRole('link', { name: new RegExp(title) });
  await expect(queueRow).toBeVisible({ timeout: 20_000 });
  await expect(camila.page.getByText(`Novo chamado ${ticket.number}`).first()).toBeVisible(); // toast da notificação em tempo real

  // 4) Agente assume o chamado
  await queueRow.click();
  await expect(camila.page.getByRole('heading', { name: title })).toBeVisible();
  await camila.page.getByRole('button', { name: 'Assumir chamado' }).click();
  await expect(camila.page.getByText('Em atendimento', { exact: true }).first()).toBeVisible();

  // Solicitante vê a atribuição em tempo real
  await expect(ana.page.getByText('Em atendimento', { exact: true }).first()).toBeVisible({ timeout: 20_000 });
  await expect(ana.page.getByText('Camila Torres').first()).toBeVisible();

  // 5) Agente responde publicamente (e deixa uma nota interna)
  await camila.page.getByLabel('Nova mensagem').fill(publicReply);
  await camila.page.getByRole('button', { name: 'Enviar' }).click();
  await expect(camila.page.getByText(publicReply)).toBeVisible();

  await camila.page.getByLabel(/^Nota interna/).check();
  await camila.page.getByLabel('Nova nota interna').fill(internalNote);
  await camila.page.getByRole('button', { name: 'Salvar nota' }).click();
  await expect(camila.page.getByText(internalNote)).toBeVisible();
  await expect(camila.page.getByText('Nota interna — visível só para a equipe')).toBeVisible();

  // 6) Solicitante recebe a resposta pública — e jamais a nota interna
  await expect(ana.page.getByText(publicReply)).toBeVisible({ timeout: 20_000 });
  await expect(ana.page.getByText(internalNote)).toHaveCount(0);
  await ana.page.reload();
  await expect(ana.page.getByText(publicReply)).toBeVisible();
  await expect(ana.page.getByText(internalNote)).toHaveCount(0);

  // 7) Agente resolve
  await camila.page.getByRole('button', { name: 'Marcar como resolvido' }).click();
  await expect(camila.page.getByText('Resolvido', { exact: true }).first()).toBeVisible();

  // 8) Solicitante acompanha: status atualizado e ações de confirmação/reabertura
  await expect(ana.page.getByText('Resolvido', { exact: true }).first()).toBeVisible({ timeout: 20_000 });
  await expect(ana.page.getByRole('button', { name: 'Reabrir chamado' })).toBeVisible();
  await expect(ana.page.getByRole('button', { name: 'Confirmar solução e fechar' })).toBeVisible();
  await ana.page.getByRole('tab', { name: 'Histórico' }).click();
  await expect(ana.page.getByText('Chamado resolvido: Em atendimento → Resolvido')).toBeVisible();

  // 9) Administrador consulta os indicadores (números vêm do banco e refletem o chamado resolvido)
  const admin = await openSession(browser, 'admin');
  await expect(admin.page.getByRole('heading', { name: 'Visão geral' })).toBeVisible();
  const total = admin.page.getByRole('link', { name: /Total de chamados/ });
  await expect(total).toContainText(String(before.total + 1));
  const resolved = admin.page.getByRole('link', { name: /Resolvidos/ });
  await expect(resolved).toContainText(String(before.resolved + before.closed + 1));
  await expect(admin.page.getByRole('img', { name: /Chamados por categoria/ })).toBeVisible();

  await adminApi.api.dispose();
  for (const s of [ana, camila, admin]) await s.context.close();
});

test('solicitante reabre um chamado resolvido e o agente é avisado', async ({ browser }) => {
  const title = uniqueTitle('E2E reabertura');
  const ana = await openSession(browser, 'ana');
  const camila = await openSession(browser, 'camila');

  const { id, number } = await openTicketViaUi(ana.page, title);
  await camila.page.goto(`/tickets/${id}`);
  await camila.page.getByRole('button', { name: 'Assumir chamado' }).click();
  await camila.page.getByRole('button', { name: 'Marcar como resolvido' }).click();
  await expect(camila.page.getByText('Resolvido', { exact: true }).first()).toBeVisible();

  await ana.page.reload();
  await ana.page.getByRole('button', { name: 'Reabrir chamado' }).click();
  await expect(ana.page.getByText('Em atendimento', { exact: true }).first()).toBeVisible();

  await expect(camila.page.getByText('Em atendimento', { exact: true }).first()).toBeVisible({ timeout: 20_000 });
  await camila.page.goto('/notifications');
  await expect(camila.page.getByText(new RegExp(`Chamado ${number} mudou para Em atendimento`))).toBeVisible();

  for (const s of [ana, camila]) await s.context.close();
});

test('o solicitante cancela o próprio chamado somente após confirmar', async ({ browser }) => {
  const ana = await openSession(browser, 'ana');
  const { number } = await openTicketViaUi(ana.page, uniqueTitle('E2E cancelamento'));

  await ana.page.getByRole('button', { name: 'Cancelar chamado' }).click();
  const dialog = ana.page.getByRole('dialog', { name: 'Cancelar este chamado?' });
  await dialog.getByRole('button', { name: 'Voltar' }).click();
  await expect(ana.page.getByText('Novo', { exact: true }).first()).toBeVisible(); // nada mudou

  await ana.page.getByRole('button', { name: 'Cancelar chamado' }).click();
  await ana.page.getByRole('dialog').getByRole('button', { name: 'Cancelar chamado' }).click();
  await expect(ana.page.getByText('Cancelado', { exact: true }).first()).toBeVisible();
  await expect(ana.page.getByText(/finalizado e não aceita novas mensagens/)).toBeVisible();
  expect(number).toBeTruthy();
  await ana.context.close();
});
