import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router-dom';
import { installFetch, makeSession, makeUser, renderWithProviders } from '@/test/utils';
import NewTicketPage from './NewTicketPage';

const responder = ({ method, path }: { method: string; path: string }) => {
  if (method === 'GET' && path === '/categories') return { json: [{ id: 'c1', name: 'Hardware', description: null, isActive: true, defaultTeamId: null }] };
  if (method === 'GET' && path === '/priorities') return { json: [{ id: 'p3', name: 'Alta', level: 3, firstResponseMinutes: 60, resolutionMinutes: 480, isActive: true }] };
  if (method === 'POST' && path === '/tickets') return { status: 201, json: { id: 'novo-1' } };
  if (method === 'POST' && path === '/tickets/novo-1/attachments') return { status: 201, json: { id: 'att-1' } };
  return undefined;
};

function setup() {
  const fetch = installFetch(responder, { session: makeSession(makeUser('REQUESTER')) });
  renderWithProviders(
    <Routes>
      <Route path="/tickets/new" element={<NewTicketPage />} />
      <Route path="/tickets/:id" element={<h1>Detalhe do chamado</h1>} />
    </Routes>,
    { route: '/tickets/new' },
  );
  return fetch;
}

describe('NewTicketPage', () => {
  it('valida os campos antes de enviar', async () => {
    const { requests } = setup();
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Abrir chamado' }));

    expect(await screen.findByText('Descreva o assunto em ao menos 5 caracteres.')).toBeInTheDocument();
    expect(screen.getByText('Conte mais detalhes (mínimo de 10 caracteres).')).toBeInTheDocument();
    expect(screen.getByText('Escolha uma categoria.')).toBeInTheDocument();
    expect(screen.getByText('Escolha uma prioridade.')).toBeInTheDocument();
    expect(requests('POST', '/tickets')).toHaveLength(0);
  });

  it('mostra as metas de SLA da prioridade escolhida', async () => {
    setup();
    await userEvent.setup().selectOptions(await screen.findByLabelText(/Prioridade/), 'p3');
    expect(await screen.findByText(/1ª resposta em 1h • resolução em 8h/)).toBeInTheDocument();
  });

  it('abre o chamado, envia os anexos e navega para o detalhe', async () => {
    const { requests } = setup();
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText(/Assunto/), 'Notebook não liga');
    await user.selectOptions(screen.getByLabelText(/Categoria/), 'c1');
    await user.selectOptions(screen.getByLabelText(/Prioridade/), 'p3');
    await user.type(screen.getByLabelText(/Descrição/), 'Não acende mais desde ontem à tarde.');
    await user.upload(document.querySelector('input[type=file]')!, new File(['conteudo'], 'foto.png', { type: 'image/png' }));
    expect(screen.getByText(/foto\.png/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Abrir chamado' }));

    expect(await screen.findByRole('heading', { name: 'Detalhe do chamado' })).toBeInTheDocument();
    expect(requests('POST', '/tickets')[0].body).toEqual({
      title: 'Notebook não liga',
      description: 'Não acende mais desde ontem à tarde.',
      categoryId: 'c1',
      priorityId: 'p3',
    });
    await waitFor(() => expect(requests('POST', '/tickets/novo-1/attachments')).toHaveLength(1));
  });

  it('recusa arquivos acima do limite sem enviá-los', async () => {
    setup();
    const big = new File([new Uint8Array(11 * 1024 * 1024)], 'enorme.pdf', { type: 'application/pdf' });
    await userEvent.setup({ applyAccept: false }).upload(await screen.findByLabelText(/Selecionar arquivos/), big);

    expect(await screen.findByText(/"enorme\.pdf" excede/)).toBeInTheDocument();
    expect(screen.queryByText(/enorme\.pdf \(/)).not.toBeInTheDocument();
  });
});
