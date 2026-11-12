import type { HistoryAction, NotificationType, Role, SlaState, TicketStatus } from '@/types/api';

const ZONE = 'America/Sao_Paulo';

const dateTime = new Intl.DateTimeFormat('pt-BR', { timeZone: ZONE, dateStyle: 'short', timeStyle: 'short' });
const dateOnly = new Intl.DateTimeFormat('pt-BR', { timeZone: ZONE, dateStyle: 'medium' });
const timeOnly = new Intl.DateTimeFormat('pt-BR', { timeZone: ZONE, timeStyle: 'short' });
const relative = new Intl.RelativeTimeFormat('pt-BR', { numeric: 'auto' });

export const formatDateTime = (iso: string | null | undefined): string => (iso ? dateTime.format(new Date(iso)) : '—');
export const formatDate = (iso: string | null | undefined): string => (iso ? dateOnly.format(new Date(iso)) : '—');
export const formatTime = (iso: string | null | undefined): string => (iso ? timeOnly.format(new Date(iso)) : '—');

/** "há 5 minutos", "ontem"... */
export function formatRelative(iso: string, now = Date.now()): string {
  const diffSeconds = Math.round((new Date(iso).getTime() - now) / 1000);
  const abs = Math.abs(diffSeconds);
  if (abs < 60) return relative.format(diffSeconds, 'second');
  if (abs < 3600) return relative.format(Math.round(diffSeconds / 60), 'minute');
  if (abs < 86_400) return relative.format(Math.round(diffSeconds / 3600), 'hour');
  if (abs < 2_592_000) return relative.format(Math.round(diffSeconds / 86_400), 'day');
  return formatDate(iso);
}

/** 90 -> "1h 30min"; 2880 -> "2d". */
export function formatMinutes(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) return '—';
  const total = Math.round(minutes);
  if (total < 1) return '< 1min';
  const days = Math.floor(total / 1440);
  const hours = Math.floor((total % 1440) / 60);
  const mins = total % 60;
  const parts: string[] = [];
  if (days) parts.push(`${days}d`);
  if (hours) parts.push(`${hours}h`);
  if (mins && !days) parts.push(`${mins}min`);
  return parts.join(' ') || '0min';
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export const formatPercent = (value: number | null | undefined): string =>
  value === null || value === undefined ? '—' : `${value.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%`;

export const initials = (name: string): string =>
  name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join('');

export const STATUS_LABEL: Record<TicketStatus, string> = {
  NEW: 'Novo',
  IN_PROGRESS: 'Em atendimento',
  WAITING_FOR_USER: 'Aguardando usuário',
  RESOLVED: 'Resolvido',
  CLOSED: 'Fechado',
  CANCELLED: 'Cancelado',
};

export const ACTIVE_STATUSES: TicketStatus[] = ['NEW', 'IN_PROGRESS', 'WAITING_FOR_USER'];

export const SLA_LABEL: Record<SlaState, string> = {
  ON_TRACK: 'No prazo',
  AT_RISK: 'Em risco',
  OVERDUE: 'Atrasado',
  MET: 'Prazo cumprido',
  MISSED: 'Prazo violado',
  NOT_APPLICABLE: 'Não se aplica',
};

export const ROLE_LABEL: Record<Role, string> = {
  ADMIN: 'Administrador',
  AGENT: 'Agente',
  REQUESTER: 'Solicitante',
};

export const NOTIFICATION_LABEL: Record<NotificationType, string> = {
  NEW_TICKET: 'Novo chamado',
  TICKET_ASSIGNED: 'Atribuição',
  NEW_MESSAGE: 'Nova mensagem',
  STATUS_CHANGED: 'Mudança de status',
  SLA_NEAR_DUE: 'SLA próximo do vencimento',
  SLA_BREACHED: 'SLA violado',
};

export const HISTORY_LABEL: Record<HistoryAction, string> = {
  CREATED: 'Chamado criado',
  STATUS_CHANGED: 'Status alterado',
  PRIORITY_CHANGED: 'Prioridade alterada',
  ASSIGNED: 'Responsável alterado',
  TEAM_CHANGED: 'Equipe alterada',
  RESOLVED: 'Chamado resolvido',
  REOPENED: 'Chamado reaberto',
  CLOSED: 'Chamado fechado',
  CANCELLED: 'Chamado cancelado',
  SLA_BREACHED: 'SLA violado',
};

export const SLA_BREACH_LABEL: Record<string, string> = {
  FIRST_RESPONSE: 'primeira resposta',
  RESOLUTION: 'resolução',
};

/** Ações do usuário por status de destino (rótulo do botão). */
export const STATUS_ACTION_LABEL: Record<TicketStatus, string> = {
  NEW: 'Marcar como novo',
  IN_PROGRESS: 'Retomar atendimento',
  WAITING_FOR_USER: 'Aguardar usuário',
  RESOLVED: 'Marcar como resolvido',
  CLOSED: 'Fechar chamado',
  CANCELLED: 'Cancelar chamado',
};

export function historyDescription(entry: { action: HistoryAction; oldValue: string | null; newValue: string | null }): string {
  const statusName = (code: string | null) => (code && code in STATUS_LABEL ? STATUS_LABEL[code as TicketStatus] : (code ?? '—'));
  switch (entry.action) {
    case 'CREATED':
      return 'Chamado aberto';
    case 'STATUS_CHANGED':
    case 'RESOLVED':
    case 'REOPENED':
    case 'CLOSED':
    case 'CANCELLED':
      return `${HISTORY_LABEL[entry.action]}: ${statusName(entry.oldValue)} → ${statusName(entry.newValue)}`;
    case 'PRIORITY_CHANGED':
      return `Prioridade: ${entry.oldValue ?? '—'} → ${entry.newValue ?? '—'}`;
    case 'TEAM_CHANGED':
      return `Equipe: ${entry.oldValue ?? 'nenhuma'} → ${entry.newValue ?? 'nenhuma'}`;
    case 'ASSIGNED':
      return entry.newValue ? `Atribuído a ${entry.newValue}` : `Responsável removido (era ${entry.oldValue ?? '—'})`;
    case 'SLA_BREACHED':
      return `SLA de ${SLA_BREACH_LABEL[entry.newValue ?? ''] ?? 'atendimento'} violado`;
    default:
      return HISTORY_LABEL[entry.action];
  }
}
