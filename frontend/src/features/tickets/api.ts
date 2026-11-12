import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { useApiMutation } from '@/lib/query';
import { usePollingInterval } from '@/app/realtime';
import type { Attachment, HistoryEntry, Paged, TicketDetail, TicketListItem, TicketMessage, TicketStatus } from '@/types/api';

export interface TicketFilters {
  search?: string;
  status?: string;
  priorityId?: string;
  categoryId?: string;
  teamId?: string;
  assignedToId?: string;
  unassigned?: boolean;
  mine?: boolean;
  overdue?: boolean;
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}

export function useTickets(filters: TicketFilters) {
  const refetchInterval = usePollingInterval();
  return useQuery({
    queryKey: ['tickets', filters],
    queryFn: ({ signal }) => api<Paged<TicketListItem>>('/tickets', { query: { ...filters }, signal }),
    placeholderData: keepPreviousData,
    refetchInterval,
  });
}

export function useTicket(id: string) {
  const refetchInterval = usePollingInterval();
  return useQuery({ queryKey: ['ticket', id], queryFn: ({ signal }) => api<TicketDetail>(`/tickets/${id}`, { signal }), refetchInterval });
}

export function useMessages(id: string) {
  const refetchInterval = usePollingInterval();
  return useQuery({ queryKey: ['messages', id], queryFn: ({ signal }) => api<TicketMessage[]>(`/tickets/${id}/messages`, { signal }), refetchInterval });
}

export function useHistory(id: string, enabled = true) {
  return useQuery({ queryKey: ['history', id], queryFn: ({ signal }) => api<HistoryEntry[]>(`/tickets/${id}/history`, { signal }), enabled });
}

export function useAttachments(id: string) {
  return useQuery({ queryKey: ['attachments', id], queryFn: ({ signal }) => api<Attachment[]>(`/tickets/${id}/attachments`, { signal }) });
}

const ticketKeys = [['tickets'], ['dashboard']];
const detailKeys = (id: string) => [...ticketKeys, ['ticket', id], ['history', id]];

export interface CreateTicketInput {
  title: string;
  description: string;
  categoryId: string;
  priorityId: string;
}

export const useCreateTicket = () =>
  useApiMutation<CreateTicketInput, TicketDetail>({ method: 'POST', path: () => '/tickets', invalidate: ticketKeys });

export interface UpdateTicketInput {
  title?: string;
  description?: string;
  categoryId?: string;
  priorityId?: string;
  teamId?: string;
}

export const useUpdateTicket = (id: string) =>
  useApiMutation<UpdateTicketInput, TicketDetail>({ method: 'PATCH', path: () => `/tickets/${id}`, invalidate: detailKeys(id) });

export const useChangeStatus = (id: string) =>
  useApiMutation<TicketStatus, TicketDetail>({
    method: 'PATCH',
    path: () => `/tickets/${id}/status`,
    body: (status) => ({ status }),
    invalidate: detailKeys(id),
  });

export const useAssign = (id: string) =>
  useApiMutation<string | null, TicketDetail>({
    method: 'PATCH',
    path: () => `/tickets/${id}/assignment`,
    body: (assignedToId) => ({ assignedToId }),
    invalidate: detailKeys(id),
  });

export interface PostMessageInput {
  content: string;
  isInternal: boolean;
}

export const usePostMessage = (id: string) =>
  useApiMutation<PostMessageInput, TicketMessage>({
    method: 'POST',
    path: () => `/tickets/${id}/messages`,
    invalidate: [...detailKeys(id), ['messages', id]],
  });

/** Move um chamado a partir de qualquer tela (ex.: Kanban), sem hooks por id. */
export async function moveTicket(id: string, action: { assignTo: string } | { status: TicketStatus }): Promise<void> {
  if ('assignTo' in action) await api(`/tickets/${id}/assignment`, { method: 'PATCH', body: { assignedToId: action.assignTo } });
  else await api(`/tickets/${id}/status`, { method: 'PATCH', body: { status: action.status } });
}

export async function uploadAttachment(ticketId: string, file: File): Promise<Attachment> {
  const form = new FormData();
  form.append('file', file);
  return api<Attachment>(`/tickets/${ticketId}/attachments`, { method: 'POST', body: form });
}
