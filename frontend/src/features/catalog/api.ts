import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { useApiMutation } from '@/lib/query';
import type { Category, Priority, Team, TeamMember, User } from '@/types/api';

const longCache = { staleTime: 5 * 60_000 };

export const useCategories = () =>
  useQuery({ queryKey: ['catalog', 'categories'], queryFn: () => api<Category[]>('/categories'), ...longCache });

export const usePriorities = () =>
  useQuery({ queryKey: ['catalog', 'priorities'], queryFn: () => api<Priority[]>('/priorities'), ...longCache });

export const useTeams = (enabled = true) =>
  useQuery({ queryKey: ['catalog', 'teams'], queryFn: () => api<Team[]>('/teams'), enabled, ...longCache });

export const useAgents = (enabled = true) =>
  useQuery({ queryKey: ['catalog', 'agents'], queryFn: () => api<User[]>('/users/agents'), enabled, ...longCache });

export const useTeamMembers = (teamId: string | null) =>
  useQuery({
    queryKey: ['catalog', 'team-members', teamId],
    queryFn: () => api<TeamMember[]>(`/teams/${teamId}/members`),
    enabled: teamId !== null,
  });

const catalog = [['catalog']];

export interface CategoryInput { name: string; description: string | null; defaultTeamId: string | null }
export const useSaveCategory = (id: string | null) =>
  useApiMutation<CategoryInput, Category>({ method: id ? 'PUT' : 'POST', path: () => (id ? `/categories/${id}` : '/categories'), invalidate: catalog });
export const useToggleCategory = () =>
  useApiMutation<{ id: string; isActive: boolean }, Category>({
    method: 'PATCH', path: (v) => `/categories/${v.id}/active`, body: (v) => ({ isActive: v.isActive }), invalidate: catalog,
  });

export interface PriorityInput { name: string; level: number; firstResponseMinutes: number; resolutionMinutes: number }
export const useSavePriority = (id: string | null) =>
  useApiMutation<PriorityInput, Priority>({ method: id ? 'PUT' : 'POST', path: () => (id ? `/priorities/${id}` : '/priorities'), invalidate: [...catalog, ['reports']] });
export const useTogglePriority = () =>
  useApiMutation<{ id: string; isActive: boolean }, Priority>({
    method: 'PATCH', path: (v) => `/priorities/${v.id}/active`, body: (v) => ({ isActive: v.isActive }), invalidate: catalog,
  });

export interface TeamInput { name: string; description: string | null }
export const useSaveTeam = (id: string | null) =>
  useApiMutation<TeamInput, Team>({ method: id ? 'PUT' : 'POST', path: () => (id ? `/teams/${id}` : '/teams'), invalidate: catalog });
export const useToggleTeam = () =>
  useApiMutation<{ id: string; isActive: boolean }, Team>({
    method: 'PATCH', path: (v) => `/teams/${v.id}/active`, body: (v) => ({ isActive: v.isActive }), invalidate: catalog,
  });
export const useAddMember = (teamId: string) =>
  useApiMutation<string>({ method: 'POST', path: () => `/teams/${teamId}/members`, body: (userId) => ({ userId }), invalidate: catalog });
export const useRemoveMember = (teamId: string) =>
  useApiMutation<string>({ method: 'DELETE', path: (userId) => `/teams/${teamId}/members/${userId}`, body: () => undefined, invalidate: catalog });
