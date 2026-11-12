import type { ComponentType } from 'react';
import { createBrowserRouter, type RouteObject } from 'react-router-dom';
import { PageLoader } from '@/components/ui/states';
import { AppLayout } from '@/layouts/AppLayout';
import LoginPage from '@/pages/LoginPage';
import NotFoundPage from '@/pages/system/NotFoundPage';
import RouteError from '@/pages/system/RouteError';
import type { Role } from '@/types/api';
import { PublicOnly, RequireAuth, RequireRole } from './guards';

type PageModule = Record<string, ComponentType>;

interface PageOptions {
  /** Nome do export da página (padrão: default). */
  name?: string;
  /** Papéis permitidos; fora deles a página 403 é exibida. O backend valida de qualquer forma. */
  roles?: Role[];
}

/** Carrega a página sob demanda (code splitting) e aplica o guard de papel, quando houver. */
const page = (load: () => Promise<PageModule>, { name = 'default', roles }: PageOptions = {}): Pick<RouteObject, 'lazy'> => ({
  lazy: async () => {
    const Page = (await load())[name];
    if (!roles) return { Component: Page };
    return {
      Component: () => (
        <RequireRole roles={roles}>
          <Page />
        </RequireRole>
      ),
    };
  },
});

const STAFF: Role[] = ['ADMIN', 'AGENT'];
const ADMIN: Role[] = ['ADMIN'];

// Exibido enquanto rotas carregadas sob demanda (lazy) resolvem na primeira navegação.
const fallback = <PageLoader />;

export const routes: RouteObject[] = [
  { path: '/login', element: <PublicOnly><LoginPage /></PublicOnly>, errorElement: <RouteError />, hydrateFallbackElement: fallback },
  { path: '/forgot-password', ...page(() => import('@/pages/ForgotPasswordPage')), errorElement: <RouteError />, hydrateFallbackElement: fallback },
  { path: '/reset-password', ...page(() => import('@/pages/ResetPasswordPage')), errorElement: <RouteError />, hydrateFallbackElement: fallback },
  {
    element: <RequireAuth><AppLayout /></RequireAuth>,
    errorElement: <RouteError />,
    hydrateFallbackElement: fallback,
    children: [
      { path: '/', handle: { crumb: 'Início' }, ...page(() => import('@/pages/HomePage')) },
      { path: '/tickets', handle: { crumb: 'Chamados' }, ...page(() => import('@/pages/TicketsPage')) },
      { path: '/tickets/new', handle: { crumb: 'Abrir chamado', parents: [{ label: 'Chamados', to: '/tickets' }] }, ...page(() => import('@/pages/NewTicketPage')) },
      { path: '/tickets/:id', handle: { crumb: 'Detalhes', parents: [{ label: 'Chamados', to: '/tickets' }] }, ...page(() => import('@/pages/TicketDetailPage')) },
      { path: '/queue', handle: { crumb: 'Fila' }, ...page(() => import('@/pages/TicketsPage'), { name: 'QueuePage', roles: STAFF }) },
      { path: '/assigned', handle: { crumb: 'Atribuídos a mim' }, ...page(() => import('@/pages/TicketsPage'), { name: 'AssignedPage', roles: STAFF }) },
      { path: '/kanban', handle: { crumb: 'Kanban' }, ...page(() => import('@/pages/KanbanPage'), { roles: STAFF }) },
      { path: '/notifications', handle: { crumb: 'Notificações' }, ...page(() => import('@/pages/NotificationsPage')) },
      { path: '/profile', handle: { crumb: 'Meu perfil' }, ...page(() => import('@/pages/ProfilePage')) },
      { path: '/admin/users', handle: { crumb: 'Usuários', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/UsersPage'), { roles: ADMIN }) },
      { path: '/admin/teams', handle: { crumb: 'Equipes', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/TeamsPage'), { roles: ADMIN }) },
      { path: '/admin/categories', handle: { crumb: 'Categorias', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/CategoriesPage'), { roles: ADMIN }) },
      { path: '/admin/priorities', handle: { crumb: 'Prioridades', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/PrioritiesPage'), { roles: ADMIN }) },
      { path: '/admin/sla', handle: { crumb: 'SLA', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/SlaPage'), { roles: ADMIN }) },
      { path: '/admin/reports', handle: { crumb: 'Relatórios', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/ReportsPage'), { roles: ADMIN }) },
      { path: '/admin/audit', handle: { crumb: 'Auditoria', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/AuditPage'), { roles: ADMIN }) },
      { path: '/admin/settings', handle: { crumb: 'Configurações', parents: [{ label: 'Administração', to: '/admin/users' }] }, ...page(() => import('@/pages/admin/SettingsPage'), { roles: ADMIN }) },
      { path: '*', handle: { crumb: 'Não encontrado' }, element: <NotFoundPage /> },
    ],
  },
];

export const createRouter = () => createBrowserRouter(routes);
