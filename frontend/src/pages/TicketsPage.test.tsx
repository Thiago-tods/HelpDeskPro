import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router-dom';
import { ticketItem } from '@/test/fixtures';
import { installFetch, makeSession, makeUser, paged, renderWithProviders, type Responder } from '@/test/utils';
import TicketsPage from './TicketsPage';

const catalog: Responder = ({ method, path }) => {
  if (method !== 'GET') return undefined;
  if (path === '/categories') return { json: [{ id: 'c1', name: 'Hardware', description: null, isActive: true, defaultTeamId: null }] };
  if (path === '/priorities') return { json: [{ id: 'p2', name: 'Média', level: 2, firstResponseMinutes: 240, resolutionMinutes: 2880, isActive: true }] };
  if (path === '/teams') return { json: [{ id: 'team-1', name: 'Suporte N1', description: null, isActive: true, memberCount: 2 }] };
  if (path === '/users/agents') return { json: [{ id: 'a1', fullName: 'Camila Torres', email: 'c@x', role: 'AGENT', isActive: true }] };
  return undefined;
};

function setup(role: 'ADMIN' | 'REQUESTER', list: Responder) {
  const fetch = installFetch((call) => catalog(call) ?? list(call), { session: makeSession(makeUser(role)) });
  renderWithProviders(
    <Routes>
      <Route path="/tickets" element={<TicketsPage />} />
    </Routes>,
    { route: '/tickets' },
  );
  return fetch;
}

const ticketRequests = (calls: { method: string; path: string; url: URL }[]) => calls.filter((c) => c.method === 'GET' && c.path === '/tickets');

describe('TicketsPage', () => {
  it('lista os chamados com status, prioridade e situação de SLA', async () => {
    setup('ADMIN', ({ path }) =>
      path === '/tickets' ? { json: paged([ticketItem({ slaState: 'OVERDUE', status: 'IN_PROGRESS', assignedToName: 'Camila Torres' })]) } : undefined,
    );

    const link = await screen.findByRole('link', { name: /Impressora travando/ });
    expect(link).toHaveAttribute('href', '/tickets/ticket-1');
    expect(screen.getAllByText('Em atendimento').length).toBeGreaterThan(0);
    expect(screen.getByText('Atrasado')).toBeInTheDocument();
    expect(screen.getByText('Camila Torres', { selector: 'td' })).toBeInTheDocument();
  });

  it('busca com debounce: digitar rápido gera uma única consulta com o termo completo', async () => {
    const { calls } = setup('ADMIN', ({ path }) => (path === '/tickets' ? { json: paged([ticketItem()]) } : undefined));
    await screen.findByRole('link', { name: /Impressora travando/ });
    const before = ticketRequests(calls).length;

    await userEvent.setup().type(screen.getByLabelText('Buscar'), 'rede');

    await waitFor(() => expect(ticketRequests(calls).some((c) => c.url.searchParams.get('search') === 'rede')).toBe(true), { timeout: 3000 });
    const withSearch = ticketRequests(calls).slice(before).filter((c) => c.url.searchParams.has('search'));
    expect(withSearch).toHaveLength(1);
  });

  it('combina filtros (status, atrasados) e volta à página 1', async () => {
    const { calls } = setup('ADMIN', ({ path }) => (path === '/tickets' ? { json: paged([ticketItem()], { totalCount: 40, totalPages: 3 }) } : undefined));
    await screen.findByRole('link', { name: /Impressora travando/ });
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'Próxima página' }));
    await waitFor(() => expect(ticketRequests(calls).at(-1)!.url.searchParams.get('page')).toBe('2'));

    await user.selectOptions(screen.getByLabelText('Status'), 'IN_PROGRESS');
    await user.click(screen.getByLabelText('Somente atrasados'));

    await waitFor(() => {
      const last = ticketRequests(calls).at(-1)!.url.searchParams;
      expect(last.get('status')).toBe('IN_PROGRESS');
      expect(last.get('overdue')).toBe('true');
      expect(last.get('page')).toBe('1');
    });
  });

  it('permite ordenar por prioridade e envia o parâmetro validado pelo servidor', async () => {
    const { calls } = setup('ADMIN', ({ path }) => (path === '/tickets' ? { json: paged([ticketItem()]) } : undefined));
    await screen.findByRole('link', { name: /Impressora travando/ });

    await userEvent.setup().selectOptions(screen.getByLabelText('Ordenar por'), 'priority');
    await waitFor(() => expect(ticketRequests(calls).at(-1)!.url.searchParams.get('sortBy')).toBe('priority'));
  });

  it('exibe estado vazio com orientação quando os filtros não retornam nada', async () => {
    setup('ADMIN', ({ path, url }) => (path === '/tickets' ? { json: paged(url.searchParams.get('priorityId') ? [] : [ticketItem()]) } : undefined));
    await screen.findByRole('link', { name: /Impressora travando/ });

    await userEvent.setup().selectOptions(await screen.findByLabelText('Prioridade'), 'p2');
    expect(await screen.findByText('Nenhum chamado encontrado')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Limpar filtros' })).toBeInTheDocument();
  });

  it('mostra erro com opção de tentar novamente', async () => {
    let attempts = 0;
    setup('ADMIN', ({ path }) => {
      if (path !== '/tickets') return undefined;
      attempts += 1;
      return attempts <= 3 ? { status: 500, json: { title: 'Erro inesperado' } } : { json: paged([ticketItem()]) };
    });

    expect(await screen.findByText('Não foi possível carregar', undefined, { timeout: 8000 })).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(await screen.findByRole('link', { name: /Impressora travando/ })).toBeInTheDocument();
  }, 15000);

  it('solicitante vê "Meus chamados" sem filtros de equipe/responsável nem colunas de pessoas', async () => {
    setup('REQUESTER', ({ path }) => (path === '/tickets' ? { json: paged([ticketItem()]) } : undefined));

    expect(await screen.findByRole('heading', { name: 'Meus chamados' })).toBeInTheDocument();
    await screen.findByRole('link', { name: /Impressora travando/ });
    expect(screen.queryByLabelText('Equipe')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Responsável')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Somente atrasados')).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Solicitante' })).not.toBeInTheDocument();
  });
});
