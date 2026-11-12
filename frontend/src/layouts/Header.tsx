import { Link, useMatches, useNavigate } from 'react-router-dom';
import { Bell, ChevronRight, LogOut, Menu, Moon, Sun, User as UserIcon, Wifi, WifiOff } from 'lucide-react';
import { useAuth, useCurrentUser } from '@/app/auth';
import { useRealtimeStatus } from '@/app/realtime';
import { useTheme } from '@/app/theme';
import { Avatar } from '@/components/common';
import { IconButton } from '@/components/ui/button';
import { DropdownContent, DropdownItem, DropdownLabel, DropdownMenu, DropdownSeparator, DropdownTrigger } from '@/components/ui/dropdown';
import { useMarkAllRead, useMarkRead, useNotifications, useUnreadCount } from '@/features/insights/api';
import { formatRelative, ROLE_LABEL } from '@/lib/format';
import { cn } from '@/lib/cn';

export interface RouteHandle {
  crumb?: string;
  /** Trilha intermediária entre o início e a página atual. */
  parents?: { label: string; to: string }[];
}

function Breadcrumbs() {
  const matches = useMatches();
  const leaf = [...matches].reverse().find((m) => (m.handle as RouteHandle | undefined)?.crumb);
  const handle = leaf?.handle as RouteHandle | undefined;

  const crumbs: { path: string; label: string }[] = [];
  if (leaf && handle?.crumb) {
    if (leaf.pathname !== '/') crumbs.push({ path: '/', label: 'Início' });
    for (const parent of handle.parents ?? []) crumbs.push({ path: parent.to, label: parent.label });
    crumbs.push({ path: leaf.pathname, label: handle.crumb });
  }

  return (
    <nav aria-label="Navegação estrutural" className="min-w-0">
      <ol className="flex items-center gap-1.5 text-[13px] text-muted">
        {crumbs.map((c, i) => (
          <li key={`${i}-${c.path}`} className="flex min-w-0 items-center gap-1.5">
            {i > 0 && <ChevronRight className="size-3.5 shrink-0" aria-hidden />}
            {i === crumbs.length - 1 ? (
              <span aria-current="page" className="truncate font-medium text-text">{c.label}</span>
            ) : (
              <Link to={c.path} className="truncate hover:text-text">{c.label}</Link>
            )}
          </li>
        ))}
      </ol>
    </nav>
  );
}

function RealtimeIndicator() {
  const status = useRealtimeStatus();
  const connected = status === 'connected';
  const label = connected ? 'Tempo real ativo' : status === 'connecting' ? 'Conectando ao tempo real…' : 'Tempo real indisponível: atualizando periodicamente';
  return (
    <span title={label} role="status" className={cn('hidden items-center gap-1.5 rounded-full px-2.5 py-1 text-xs sm:inline-flex', connected ? 'bg-success-soft text-success' : 'bg-warning-soft text-warning')}>
      {connected ? <Wifi className="size-3.5" aria-hidden /> : <WifiOff className="size-3.5" aria-hidden />}
      <span>{connected ? 'Ao vivo' : status === 'connecting' ? 'Conectando' : 'Offline'}</span>
      <span className="sr-only">{label}</span>
    </span>
  );
}

function NotificationsBell() {
  const navigate = useNavigate();
  const unread = useUnreadCount();
  const list = useNotifications(1, false, 6);
  const markRead = useMarkRead();
  const markAll = useMarkAllRead();
  const count = unread.data ?? 0;

  return (
    <DropdownMenu>
      <DropdownTrigger asChild>
        <button
          type="button"
          aria-label={count > 0 ? `Notificações: ${count} não lidas` : 'Notificações'}
          className="relative inline-flex size-9 items-center justify-center rounded-lg hover:bg-surface-2"
        >
          <Bell className="size-5" aria-hidden />
          {count > 0 && (
            <span aria-hidden className="absolute -top-0.5 -right-0.5 grid min-w-4.5 place-items-center rounded-full bg-danger px-1 text-[10px] font-bold text-accent-fg">
              {count > 99 ? '99+' : count}
            </span>
          )}
        </button>
      </DropdownTrigger>
      {/* Um menu só contém itens de menu: título é um rótulo e a ação em lote é um item (nada de botões soltos). */}
      <DropdownContent className="w-[min(22rem,calc(100vw-2rem))] p-1.5">
        <DropdownLabel className="text-[13px] font-semibold text-text">Notificações</DropdownLabel>
        <DropdownItem
          disabled={count === 0 || markAll.isPending}
          className="text-accent data-[disabled]:text-muted"
          onSelect={(event) => {
            event.preventDefault(); // mantém o menu aberto para ver o resultado
            markAll.mutate();
          }}
        >
          Marcar todas como lidas
        </DropdownItem>
        <DropdownSeparator />
        <div className="max-h-72 overflow-y-auto">
          {list.data?.items.length === 0 && <DropdownLabel className="py-6 text-center text-[13px]">Nada por aqui ainda.</DropdownLabel>}
          {list.data?.items.map((n) => (
            <DropdownItem
              key={n.id}
              className="items-start"
              onSelect={() => {
                if (!n.isRead) markRead.mutate(n.id);
                if (n.ticketId) navigate(`/tickets/${n.ticketId}`);
              }}
            >
              <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', n.isRead ? 'bg-transparent' : 'bg-accent')} />
              <span className="min-w-0">
                <span className={cn('block text-[13px]', !n.isRead && 'font-semibold')}>{n.message}</span>
                <span className="text-xs text-muted">{formatRelative(n.createdAt)}</span>
              </span>
            </DropdownItem>
          ))}
        </div>
        <DropdownSeparator />
        <DropdownItem className="justify-center text-accent" onSelect={() => navigate('/notifications')}>
          Ver todas
        </DropdownItem>
      </DropdownContent>
    </DropdownMenu>
  );
}

function ThemeToggle() {
  const { theme, toggle } = useTheme();
  const dark = theme === 'dark';
  return (
    <IconButton label={dark ? 'Mudar para o tema claro' : 'Mudar para o tema escuro'} aria-pressed={dark} onClick={toggle}>
      {dark ? <Sun className="size-5" aria-hidden /> : <Moon className="size-5" aria-hidden />}
    </IconButton>
  );
}

function UserMenu() {
  const user = useCurrentUser();
  const { logout } = useAuth();
  const navigate = useNavigate();

  return (
    <DropdownMenu>
      <DropdownTrigger asChild>
        <button type="button" className="flex items-center gap-2.5 rounded-lg p-1 pr-2 hover:bg-surface-2">
          <span className="sr-only">Menu do usuário:</span>
          <Avatar name={user.fullName} />
          <span className="hidden text-left leading-tight md:block">
            <span className="block max-w-36 truncate text-[13px] font-medium">{user.fullName}</span>
            <span className="block text-xs text-muted">{ROLE_LABEL[user.role]}</span>
          </span>
        </button>
      </DropdownTrigger>
      <DropdownContent>
        <DropdownLabel>{user.email}</DropdownLabel>
        <DropdownSeparator />
        <DropdownItem onSelect={() => navigate('/profile')}>
          <UserIcon className="size-4" aria-hidden /> Meu perfil
        </DropdownItem>
        <DropdownItem onSelect={() => void logout().then(() => navigate('/login'))}>
          <LogOut className="size-4" aria-hidden /> Sair
        </DropdownItem>
      </DropdownContent>
    </DropdownMenu>
  );
}

export function Header({ onMenu }: { onMenu: () => void }) {
  return (
    <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-border bg-surface/95 px-4 backdrop-blur md:px-6">
      <IconButton label="Abrir menu de navegação" className="lg:hidden" onClick={onMenu}>
        <Menu className="size-5" aria-hidden />
      </IconButton>
      <div className="flex-1 min-w-0">
        <Breadcrumbs />
      </div>
      <RealtimeIndicator />
      <ThemeToggle />
      <NotificationsBell />
      <UserMenu />
    </header>
  );
}
