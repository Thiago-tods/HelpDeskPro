import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router-dom';
import { message, ticketDetail } from '@/test/fixtures';
import { installFetch, makeSession, makeUser, renderWithProviders, type MockResponse } from '@/test/utils';
import type { Role, TicketDetail, TicketMessage } from '@/types/api';
import TicketDetailPage from './TicketDetailPage';

interface Setup {
  role: Role;
  ticket: TicketDetail;
  messages?: TicketMessage[];
  userId?: string;
}

function setup({ role, ticket, messages = [], userId }: Setup) {
  const user = makeUser(role, userId ? { id: userId } : {});
  const fetch = installFetch(({ method, path }): MockResponse | undefined => {
    if (method === 'GET' && path === '/tickets/ticket-1') return { json: ticket };
    if (method === 'GET' && path === '/tickets/ticket-1/messages') return { json: messages };
    if (method === 'GET' && path === '/tickets/ticket-1/attachments') return { json: [] };
    if (method === 'GET' && path === '/tickets/ticket-1/history') return { json: [] };
    if (method === 'GET' && path === '/teams/team-1/members') return { json: [{ userId: 'a1', fullName: 'Camila Torres', email: 'c@x', joinedAt: '2026-01-01T00:00:00Z' }] };
    if (method === 'PATCH' && path.startsWith('/tickets/ticket-1')) return { json: ticket };
    if (method === 'POST' && path === '/tickets/ticket-1/messages') return { status: 201, json: message({ content: 'ok' }) };
    return undefined;
  }, { session: makeSession(user) });

  renderWithProviders(
    <Routes>
      <Route path="/tickets/:id" element={<TicketDetailPage />} />
    </Routes>,
    { route: '/tickets/ticket-1' },
  );
  return fetch;
}

describe('TicketDetailPage — permissões visuais guiadas pelo servidor', () => {
  it('solicitante: sem nota interna, sem assumir/editar; só as ações liberadas pelo servidor', async () => {
    setup({ role: 'REQUESTER', ticket: ticketDetail({ allowedStatuses: ['CANCELLED'] }) });

    expect(await screen.findByRole('heading', { name: 'Impressora travando' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar chamado' })).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Nota interna/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Assumir chamado' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar chamado' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Responsável')).not.toBeInTheDocument();
  });

  it('cancelar exige confirmação e só chama a API depois dela', async () => {
    const { requests } = setup({ role: 'REQUESTER', ticket: ticketDetail({ allowedStatuses: ['CANCELLED'] }) });
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Cancelar chamado' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar este chamado?' });
    expect(requests('PATCH', '/tickets/ticket-1/status')).toHaveLength(0);

    await user.click(within(dialog).getByRole('button', { name: 'Voltar' }));
    expect(requests('PATCH', '/tickets/ticket-1/status')).toHaveLength(0);

    await user.click(screen.getByRole('button', { name: 'Cancelar chamado' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancelar chamado' }));
    await waitFor(() => expect(requests('PATCH', '/tickets/ticket-1/status')[0]?.body).toEqual({ status: 'CANCELLED' }));
  });

  it('rótulos contextuais: reabrir e confirmar solução vêm do estado retornado pelo servidor', async () => {
    setup({ role: 'REQUESTER', ticket: ticketDetail({ status: 'RESOLVED', allowedStatuses: ['IN_PROGRESS', 'CLOSED'] }) });
    expect(await screen.findByRole('button', { name: 'Reabrir chamado' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirmar solução e fechar' })).toBeInTheDocument();
  });

  it('agente que não é responsável: pode assumir, mas só cria nota interna', async () => {
    const { requests } = setup({ role: 'AGENT', userId: 'agente-1', ticket: ticketDetail({ canAssignToMe: true }) });
    const user = userEvent.setup();

    const checkbox = await screen.findByLabelText(/^Nota interna/);
    expect(checkbox).toBeChecked();
    expect(checkbox).toBeDisabled();
    expect(screen.getByText('Assuma o chamado para responder ao solicitante.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Assumir chamado' }));
    await waitFor(() => expect(requests('PATCH', '/tickets/ticket-1/assignment')[0]?.body).toEqual({ assignedToId: 'agente-1' }));
  });

  it('agente responsável: alterna entre resposta pública e nota interna', async () => {
    const { requests } = setup({
      role: 'AGENT', userId: 'agente-1',
      ticket: ticketDetail({ assignedToId: 'agente-1', assignedToName: 'Eu', status: 'IN_PROGRESS', allowedStatuses: ['WAITING_FOR_USER', 'RESOLVED'], canEdit: true }),
    });
    const user = userEvent.setup();

    const checkbox = await screen.findByLabelText(/^Nota interna/);
    expect(checkbox).toBeEnabled();
    expect(checkbox).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Aguardar usuário' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar chamado' })).toBeInTheDocument();

    await user.type(screen.getByLabelText('Nova mensagem'), 'Resposta pública');
    await user.click(screen.getByRole('button', { name: 'Enviar' }));
    await waitFor(() => expect(requests('POST', '/tickets/ticket-1/messages')[0]?.body).toEqual({ content: 'Resposta pública', isInternal: false }));

    await user.click(checkbox);
    await user.type(await screen.findByLabelText('Nova nota interna'), 'Falar com o fornecedor');
    await user.click(screen.getByRole('button', { name: 'Salvar nota' }));
    await waitFor(() => expect(requests('POST', '/tickets/ticket-1/messages')[1]?.body).toEqual({ content: 'Falar com o fornecedor', isInternal: true }));
  });

  it('destaca notas internas na conversa da equipe', async () => {
    setup({ role: 'AGENT', ticket: ticketDetail(), messages: [message({ id: 'n1', content: 'Segredo interno', isInternal: true }), message({ id: 'n2', content: 'Olá, tudo bem?' })] });

    expect(await screen.findByText('Segredo interno')).toBeInTheDocument();
    expect(screen.getByText('Nota interna — visível só para a equipe')).toBeInTheDocument();
    expect(screen.getByText('Olá, tudo bem?')).toBeInTheDocument();
  });

  it('administrador escolhe o responsável entre os membros da equipe do chamado', async () => {
    const { requests } = setup({ role: 'ADMIN', ticket: ticketDetail({ canEdit: true }) });
    const user = userEvent.setup();

    const select = await screen.findByLabelText('Responsável');
    await screen.findByRole('option', { name: 'Camila Torres' });
    await user.selectOptions(select, 'a1');
    await user.click(screen.getByRole('button', { name: 'Aplicar responsável' }));
    await waitFor(() => expect(requests('PATCH', '/tickets/ticket-1/assignment')[0]?.body).toEqual({ assignedToId: 'a1' }));
  });

  it('chamado finalizado não aceita mensagens nem anexos', async () => {
    setup({ role: 'REQUESTER', ticket: ticketDetail({ status: 'CLOSED' }) });
    expect(await screen.findByText(/finalizado e não aceita novas mensagens/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Nova mensagem')).not.toBeInTheDocument();

    await userEvent.setup().click(screen.getByRole('tab', { name: /Anexos/ }));
    expect(await screen.findByText(/não é possível anexar arquivos/)).toBeInTheDocument();
  });

  it('exibe o prazo de SLA e a violação registrada pelo servidor', async () => {
    setup({
      role: 'AGENT',
      ticket: ticketDetail({
        slaState: 'OVERDUE',
        sla: {
          firstResponseDueAt: '2026-11-10T16:00:00Z', resolutionDueAt: '2026-11-12T12:00:00Z', firstRespondedAt: null,
          breachedAt: '2026-11-10T16:01:00Z', firstResponseBreachedAt: '2026-11-10T16:01:00Z', resolutionBreachedAt: null,
        },
      }),
    });
    expect(await screen.findByText('Atrasado')).toBeInTheDocument();
    expect(screen.getByText(/Ainda sem resposta da equipe • prazo violado/)).toBeInTheDocument();
  });

  it('mostra "não encontrado" quando o servidor responde 404 (sem revelar existência)', async () => {
    installFetch(({ path }) => (path === '/tickets/ticket-1' ? { status: 404, json: { title: 'Não encontrado' } } : path === '/tickets/ticket-1/messages' ? { status: 404, json: {} } : path === '/tickets/ticket-1/attachments' ? { status: 404, json: {} } : undefined), { session: makeSession(makeUser('REQUESTER')) });
    renderWithProviders(<Routes><Route path="/tickets/:id" element={<TicketDetailPage />} /></Routes>, { route: '/tickets/ticket-1' });

    expect(await screen.findByText('Chamado não encontrado')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Voltar aos chamados/ })).toHaveAttribute('href', '/tickets');
  });
});
