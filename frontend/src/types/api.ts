// Tipos espelham os DTOs da API (camelCase). Status e estados trafegam como códigos (ex.: IN_PROGRESS).

export type Role = 'ADMIN' | 'AGENT' | 'REQUESTER';

export type TicketStatus = 'NEW' | 'IN_PROGRESS' | 'WAITING_FOR_USER' | 'RESOLVED' | 'CLOSED' | 'CANCELLED';
export type SlaState = 'ON_TRACK' | 'AT_RISK' | 'OVERDUE' | 'MET' | 'MISSED' | 'NOT_APPLICABLE';

export interface User {
  id: string;
  fullName: string;
  email: string;
  role: Role;
  isActive: boolean;
}

export interface AuthSession {
  accessToken: string;
  expiresAt: string;
  user: User;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface TicketListItem {
  id: string;
  ticketNumber: string;
  title: string;
  status: TicketStatus;
  priorityId: string;
  priorityName: string;
  priorityLevel: number;
  categoryId: string;
  categoryName: string;
  teamId: string | null;
  teamName: string | null;
  requesterId: string;
  requesterName: string;
  assignedToId: string | null;
  assignedToName: string | null;
  createdAt: string;
  updatedAt: string;
  firstResponseDueAt: string | null;
  resolutionDueAt: string;
  breachedAt: string | null;
  slaState: SlaState;
}

export interface TicketSla {
  firstResponseDueAt: string;
  resolutionDueAt: string;
  firstRespondedAt: string | null;
  breachedAt: string | null;
  firstResponseBreachedAt: string | null;
  resolutionBreachedAt: string | null;
}

export interface TicketDetail extends Omit<TicketListItem, 'firstResponseDueAt' | 'resolutionDueAt' | 'breachedAt'> {
  description: string;
  firstRespondedAt: string | null;
  resolvedAt: string | null;
  closedAt: string | null;
  sla: TicketSla;
  allowedStatuses: TicketStatus[];
  canAssignToMe: boolean;
  canEdit: boolean;
}

export interface TicketMessage {
  id: string;
  ticketId: string;
  authorId: string;
  authorName: string;
  authorRole: Role;
  content: string;
  isInternal: boolean;
  createdAt: string;
}

export interface Attachment {
  id: string;
  ticketId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedById: string;
  uploadedByName: string;
  createdAt: string;
}

export type HistoryAction =
  | 'CREATED'
  | 'STATUS_CHANGED'
  | 'PRIORITY_CHANGED'
  | 'ASSIGNED'
  | 'TEAM_CHANGED'
  | 'RESOLVED'
  | 'REOPENED'
  | 'CLOSED'
  | 'CANCELLED'
  | 'SLA_BREACHED';

export interface HistoryEntry {
  id: string;
  action: HistoryAction;
  actorId: string | null;
  actorName: string;
  oldValue: string | null;
  newValue: string | null;
  createdAt: string;
}

export type NotificationType =
  | 'NEW_TICKET'
  | 'TICKET_ASSIGNED'
  | 'NEW_MESSAGE'
  | 'STATUS_CHANGED'
  | 'SLA_NEAR_DUE'
  | 'SLA_BREACHED';

export interface AppNotification {
  id: string;
  type: NotificationType;
  message: string;
  ticketId: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface Category {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  defaultTeamId: string | null;
}

export interface Priority {
  id: string;
  name: string;
  level: number;
  firstResponseMinutes: number;
  resolutionMinutes: number;
  isActive: boolean;
}

export interface Team {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  memberCount: number;
}

export interface TeamMember {
  userId: string;
  fullName: string;
  email: string;
  joinedAt: string;
}

export interface NamedCount {
  id: string;
  name: string;
  count: number;
}

export interface AgentWorkload {
  id: string;
  name: string;
  total: number;
  active: number;
  resolved: number;
  overdue: number;
}

export interface DashboardSummary {
  days: number;
  generatedAt: string;
  total: number;
  open: number;
  inProgress: number;
  waitingForUser: number;
  resolved: number;
  closed: number;
  cancelled: number;
  overdue: number;
  avgFirstResponseMinutes: number | null;
  avgResolutionMinutes: number | null;
  byPriority: NamedCount[];
  byCategory: NamedCount[];
  byAgent: AgentWorkload[];
  timeline: { date: string; created: number; resolved: number }[];
}

export interface SlaReportRow {
  priorityId: string;
  priorityName: string;
  total: number;
  firstResponseMet: number;
  firstResponseMissed: number;
  resolutionMet: number;
  resolutionMissed: number;
  avgFirstResponseMinutes: number | null;
  avgResolutionMinutes: number | null;
  complianceRate: number | null;
}

export interface SlaReport {
  from: string;
  to: string;
  overallComplianceRate: number | null;
  rows: SlaReportRow[];
}

export interface AgentReportRow {
  agentId: string;
  agentName: string;
  assigned: number;
  active: number;
  resolved: number;
  overdue: number;
  avgResolutionMinutes: number | null;
}

export interface AgentReport {
  from: string;
  to: string;
  rows: AgentReportRow[];
}

export interface AuditEntry {
  id: string;
  ticketId: string;
  ticketNumber: string;
  action: HistoryAction;
  actorId: string | null;
  actorName: string;
  oldValue: string | null;
  newValue: string | null;
  createdAt: string;
}

export interface SystemSettings {
  environment: string;
  attachments: { maxBytes: number; maxPerTicket: number; allowedExtensions: string[] };
  sla: { checkIntervalSeconds: number; warningPercent: number; calendar: string };
  auth: { accessTokenMinutes: number; refreshTokenDays: number; passwordMinLength: number; lockoutMaxFailures: number };
  corsAllowedOrigins: string[];
  authRateLimitPerMinute: number;
}

export interface TicketEvent {
  type: string;
  ticketId: string;
  ticketNumber: string;
  messageId?: string | null;
  isInternal?: boolean;
}
