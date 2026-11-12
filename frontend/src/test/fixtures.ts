import type { Responder } from './utils';
import type { TicketDetail, TicketListItem, TicketMessage } from '@/types/api';

export const ticketItem = (overrides: Partial<TicketListItem> = {}): TicketListItem => ({
  id: 'ticket-1',
  ticketNumber: 'HDP-2026-000001',
  title: 'Impressora travando',
  status: 'NEW',
  priorityId: 'p2',
  priorityName: 'Média',
  priorityLevel: 2,
  categoryId: 'c1',
  categoryName: 'Hardware',
  teamId: 'team-1',
  teamName: 'Suporte N1',
  requesterId: 'user-REQUESTER',
  requesterName: 'Ana Beatriz Lima',
  assignedToId: null,
  assignedToName: null,
  createdAt: '2026-11-10T12:00:00Z',
  updatedAt: '2026-11-10T12:30:00Z',
  firstResponseDueAt: '2026-11-10T16:00:00Z',
  resolutionDueAt: '2026-11-12T12:00:00Z',
  breachedAt: null,
  slaState: 'ON_TRACK',
  ...overrides,
});

export const ticketDetail = (overrides: Partial<TicketDetail> = {}): TicketDetail => ({
  ...ticketItem(),
  description: 'A impressora do financeiro trava ao imprimir.',
  firstRespondedAt: null,
  resolvedAt: null,
  closedAt: null,
  sla: {
    firstResponseDueAt: '2026-11-10T16:00:00Z',
    resolutionDueAt: '2026-11-12T12:00:00Z',
    firstRespondedAt: null,
    breachedAt: null,
    firstResponseBreachedAt: null,
    resolutionBreachedAt: null,
  },
  allowedStatuses: [],
  canAssignToMe: false,
  canEdit: false,
  ...overrides,
});

export const message = (overrides: Partial<TicketMessage> = {}): TicketMessage => ({
  id: 'm1',
  ticketId: 'ticket-1',
  authorId: 'user-AGENT',
  authorName: 'Camila Torres',
  authorRole: 'AGENT',
  content: 'Já estamos verificando.',
  isInternal: false,
  createdAt: '2026-11-10T13:00:00Z',
  ...overrides,
});

/** Respostas comuns exigidas pelo layout autenticado (sino de notificações). */
export const shellResponder: Responder = ({ method, path }) => {
  if (method === 'GET' && path === '/notifications/unread-count') return { json: { count: 3 } };
  if (method === 'GET' && path === '/notifications') return { json: { items: [], page: 1, pageSize: 6, totalCount: 0, totalPages: 0 } };
  return undefined;
};

export const chain = (...responders: Responder[]): Responder => (call) => {
  for (const r of responders) {
    const result = r(call);
    if (result) return result;
  }
  return undefined;
};
