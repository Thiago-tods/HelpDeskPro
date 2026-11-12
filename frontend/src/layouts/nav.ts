import {
  BarChart3, Bell, Columns3, Flag, Inbox, LayoutDashboard, PlusCircle, ScrollText, Settings, Tags, Ticket, Timer, UserCheck, Users, UsersRound,
  type LucideIcon,
} from 'lucide-react';
import type { Role } from '@/types/api';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  end?: boolean;
}

export interface NavSection {
  title?: string;
  items: NavItem[];
}

const home: NavItem = { to: '/', label: 'Início', icon: LayoutDashboard, end: true };
const notifications: NavItem = { to: '/notifications', label: 'Notificações', icon: Bell };
const newTicket: NavItem = { to: '/tickets/new', label: 'Abrir chamado', icon: PlusCircle };

/** Itens de menu por papel. É apenas conveniência de navegação: a autorização real está no backend. */
export function navFor(role: Role): NavSection[] {
  if (role === 'REQUESTER') {
    return [{ items: [home, newTicket, { to: '/tickets', label: 'Meus chamados', icon: Ticket, end: true }, notifications] }];
  }

  const attendance: NavSection = {
    title: 'Atendimento',
    items: [
      home,
      { to: '/queue', label: 'Fila', icon: Inbox },
      { to: '/assigned', label: 'Atribuídos a mim', icon: UserCheck },
      { to: '/kanban', label: 'Kanban', icon: Columns3 },
      { to: '/tickets', label: 'Todos os chamados', icon: Ticket, end: true },
      newTicket,
      notifications,
    ],
  };

  if (role === 'AGENT') return [attendance];

  return [
    attendance,
    {
      title: 'Administração',
      items: [
        { to: '/admin/users', label: 'Usuários', icon: Users },
        { to: '/admin/teams', label: 'Equipes', icon: UsersRound },
        { to: '/admin/categories', label: 'Categorias', icon: Tags },
        { to: '/admin/priorities', label: 'Prioridades', icon: Flag },
        { to: '/admin/sla', label: 'SLA', icon: Timer },
        { to: '/admin/reports', label: 'Relatórios', icon: BarChart3 },
        { to: '/admin/audit', label: 'Auditoria', icon: ScrollText },
        { to: '/admin/settings', label: 'Configurações', icon: Settings },
      ],
    },
  ];
}
