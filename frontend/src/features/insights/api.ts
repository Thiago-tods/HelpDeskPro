import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { useApiMutation } from '@/lib/query';
import { usePollingInterval } from '@/app/realtime';
import type { AgentReport, AppNotification, AuditEntry, DashboardSummary, Paged, SlaReport, SystemSettings, User } from '@/types/api';

export function useDashboard(days: number) {
  const refetchInterval = usePollingInterval();
  return useQuery({
    queryKey: ['dashboard', days],
    queryFn: ({ signal }) => api<DashboardSummary>('/dashboard/summary', { query: { days }, signal }),
    placeholderData: keepPreviousData,
    refetchInterval,
  });
}

export function useNotifications(page: number, unreadOnly: boolean, pageSize = 20) {
  return useQuery({
    queryKey: ['notifications', { page, unreadOnly, pageSize }],
    queryFn: ({ signal }) => api<Paged<AppNotification>>('/notifications', { query: { page, pageSize, unreadOnly }, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useUnreadCount() {
  const fallback = usePollingInterval();
  return useQuery({
    queryKey: ['unread-count'],
    queryFn: ({ signal }) => api<{ count: number }>('/notifications/unread-count', { signal }),
    refetchInterval: fallback || 120_000,
    select: (data) => data.count,
  });
}

const notificationKeys = [['notifications'], ['unread-count']];
export const useMarkRead = () =>
  useApiMutation<string>({ method: 'PATCH', path: (id) => `/notifications/${id}/read`, body: () => undefined, invalidate: notificationKeys });
export const useMarkAllRead = () =>
  useApiMutation<void>({ method: 'POST', path: () => '/notifications/read-all', body: () => undefined, invalidate: notificationKeys });

export interface Period { from?: string; to?: string }

export const useSlaReport = (period: Period) =>
  useQuery({ queryKey: ['reports', 'sla', period], queryFn: () => api<SlaReport>('/reports/sla', { query: { ...period } }) });

export const useAgentReport = (period: Period) =>
  useQuery({ queryKey: ['reports', 'agents', period], queryFn: () => api<AgentReport>('/reports/agents', { query: { ...period } }) });

export interface AuditFilters {
  ticketNumber?: string;
  action?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export const useAudit = (filters: AuditFilters) =>
  useQuery({
    queryKey: ['audit', filters],
    queryFn: ({ signal }) => api<Paged<AuditEntry>>('/audit', { query: { ...filters }, signal }),
    placeholderData: keepPreviousData,
  });

export const useSettings = () => useQuery({ queryKey: ['settings'], queryFn: () => api<SystemSettings>('/settings') });

export const useUsers = () => useQuery({ queryKey: ['users'], queryFn: () => api<User[]>('/users') });

export interface CreateUserInput { fullName: string; email: string; role: string; password: string }
export const useCreateUser = () =>
  useApiMutation<CreateUserInput, User>({ method: 'POST', path: () => '/users', invalidate: [['users'], ['catalog']] });
export const useUpdateUser = (id: string) =>
  useApiMutation<{ fullName: string; role: string }, User>({ method: 'PUT', path: () => `/users/${id}`, invalidate: [['users'], ['catalog']] });
export const useToggleUser = () =>
  useApiMutation<{ id: string; isActive: boolean }, User>({
    method: 'PATCH', path: (v) => `/users/${v.id}/active`, body: (v) => ({ isActive: v.isActive }), invalidate: [['users'], ['catalog']],
  });
