import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router-dom';
import { ticketItem } from '@/test/fixtures';
import { installFetch, makeSession, makeUser, paged, renderWithProviders, type MockResponse } from '@/test/utils';
import type { Role, TicketListItem } from '@/types/api';
import KanbanPage from './KanbanPage';

const free = ticketItem({ id: 't-new', ticketNumber: 'HDP-2026-000001', title: 'Livre', status: 'NEW' });
const busy = ticketItem({ id: 't-wip', ticketNumber: 'HDP-2026-000002', title: 'Em andamento', status: 'IN_PROGRESS', assignedToId: 'user-AGENT', assignedToName: 'Eu' });

function setup(role: Role, items: TicketListItem[] = [free, busy], patch?: () => MockResponse) {
  const fetch = installFetch(({ method, path }): MockResponse | undefined => {
    if (method === 'GET' && path === '/tickets') return { json: paged(items, { pageSize: 100, totalCount: items.length }) };
    if (method === 'PATCH' && path.startsWith('/tickets/')) return patch?.() ?? { json: {} };
    return undefined;
  }, { session: makeSession(makeUser(role)) });
  renderWithProviders(<Routes><Route path="/kanban" element={<KanbanPage />} /></Routes>, { route: '/kanban' });
  return fetch;
}

describe('KanbanPage', () => {
  it('distribui os chamados nas colunas por status, com contagem acessível', async () => {
    setup('AGENT');
    const novo = await screen.findByRole('region', { name: 'Novo: 1 chamados' });
    expect(within(novo).getByText('Livre')).toBeInTheDocument();
    const andamento = screen.getByRole('region', { name: 'Em atendimento: 1 chamados' });
    expect(within(andamento).getByText('Em andamento')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Resolvido: 0 chamados' })).toBeInTheDocument();
  });

  it('agente assume um chamado livre: o movimento vira atribuição a si mesmo', async () => {
    const { requests } = setup('AGENT');
    await userEvent.setup().click(await screen.findByRole('button', { name: /Assumir/ }));
    await waitFor(() => expect(requests('PATCH', '/tickets/t-new/assignment')[0]?.body).toEqual({ assignedToId: 'user-AGENT' }));
    expect(requests('PATCH', '/tickets/t-new/status')).toHaveLength(0);
  });

  it('alternativa por teclado: o seletor "Mover para" muda o status pelo endpoint de status', async () => {
    const { requests } = setup('AGENT');
    await userEvent.setup().selectOptions(await screen.findByLabelText('Mover HDP-2026-000002 para'), 'WAITING_FOR_USER');
    await waitFor(() => expect(requests('PATCH', '/tickets/t-wip/status')[0]?.body).toEqual({ status: 'WAITING_FOR_USER' }));
  });

  it('arrastar e soltar em outra coluna executa o mesmo movimento', async () => {
    const { requests } = setup('AGENT');
    await screen.findByText('Em andamento');
    const target = screen.getByRole('region', { name: /Resolvido/ });
    fireEvent.drop(target, { dataTransfer: { getData: () => 't-wip' } });
    await waitFor(() => expect(requests('PATCH', '/tickets/t-wip/status')[0]?.body).toEqual({ status: 'RESOLVED' }));
  });

  it('soltar na mesma coluna ou em "Novo" não chama a API', async () => {
    const { requests } = setup('AGENT');
    await screen.findByText('Em andamento');
    fireEvent.drop(screen.getByRole('region', { name: /Em atendimento/ }), { dataTransfer: { getData: () => 't-wip' } });
    fireEvent.drop(screen.getByRole('region', { name: /Novo/ }), { dataTransfer: { getData: () => 't-wip' } });
    await new Promise((r) => setTimeout(r, 150));
    expect(requests('PATCH', '/tickets/t-wip/status')).toHaveLength(0);
    expect(await screen.findByText('Chamados não voltam para "Novo".')).toBeInTheDocument();
  });

  it('administrador não inicia atendimento arrastando: é orientado a atribuir um responsável', async () => {
    const { calls } = setup('ADMIN');
    await screen.findByText('Livre');
    fireEvent.drop(screen.getByRole('region', { name: /Em atendimento/ }), { dataTransfer: { getData: () => 't-new' } });
    expect(await screen.findByText(/Atribua um responsável no detalhe/)).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PATCH')).toBe(false);
  });

  it('exibe o motivo quando o servidor nega o movimento (permissão ou regra de estado)', async () => {
    setup('AGENT', [free, busy], () => ({ status: 403, json: { title: 'Acesso negado', detail: 'Você não pode realizar esta mudança de status neste chamado.' } }));
    await userEvent.setup().selectOptions(await screen.findByLabelText('Mover HDP-2026-000002 para'), 'RESOLVED');
    expect(await screen.findByText('Você não pode realizar esta mudança de status neste chamado.')).toBeInTheDocument();
  });

  it('"Somente os meus" consulta com mine=true', async () => {
    const { calls } = setup('AGENT');
    await userEvent.setup().click(await screen.findByLabelText('Somente os meus'));
    await waitFor(() => expect(calls.filter((c) => c.path === '/tickets').at(-1)!.url.searchParams.get('mine')).toBe('true'));
  });

  it('avisa quando há mais chamados do que o quadro comporta', async () => {
    installFetch(({ method, path }) => (method === 'GET' && path === '/tickets' ? { json: paged([free], { pageSize: 100, totalCount: 250 }) } : undefined), { session: makeSession(makeUser('AGENT')) });
    renderWithProviders(<Routes><Route path="/kanban" element={<KanbanPage />} /></Routes>, { route: '/kanban' });
    expect(await screen.findByText(/1 chamados de maior prioridade de 250/)).toBeInTheDocument();
  });
});
