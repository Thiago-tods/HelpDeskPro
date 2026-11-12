import { useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import * as Dialog from '@radix-ui/react-dialog';
import { LifeBuoy } from 'lucide-react';
import { RealtimeProvider } from '@/app/realtime';
import { useCurrentUser } from '@/app/auth';
import { useReturnFocus } from '@/hooks/useReturnFocus';
import { cn } from '@/lib/cn';
import { Header } from './Header';
import { navFor } from './nav';

function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const user = useCurrentUser();
  const sections = navFor(user.role);

  return (
    <nav aria-label="Menu principal" className="flex h-full flex-col gap-6 overflow-y-auto p-4">
      <div className="flex items-center gap-2.5 px-2 py-1">
        <span className="grid size-9 place-items-center rounded-lg bg-accent text-accent-fg">
          <LifeBuoy className="size-5" aria-hidden />
        </span>
        <div className="leading-tight">
          <p className="text-[15px] font-semibold">HelpDesk Pro</p>
          <p className="text-xs text-muted">Nexora Tecnologia</p>
        </div>
      </div>

      {sections.map((section) => (
        <div key={section.title ?? 'main'}>
          {section.title && <p className="mb-1.5 px-3 text-[11px] font-semibold tracking-wider text-muted uppercase">{section.title}</p>}
          <ul className="flex flex-col gap-0.5">
            {section.items.map((item) => (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  end={item.end}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    cn(
                      'flex items-center gap-3 rounded-lg px-3 py-2 text-[13px] font-medium transition-colors',
                      isActive ? 'bg-accent-soft text-accent-text' : 'text-muted hover:bg-surface-2 hover:text-text',
                    )
                  }
                >
                  <item.icon className="size-[18px]" aria-hidden />
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </nav>
  );
}

export function AppLayout() {
  const location = useLocation();
  // O menu móvel guarda a rota em que foi aberto: ao navegar ele "fecha" por derivação, sem setState em efeito.
  const [drawerRoute, setDrawerRoute] = useState<string | null>(null);
  const drawerOpen = drawerRoute === location.pathname;
  const setDrawerOpen = (open: boolean) => setDrawerRoute(open ? location.pathname : null);
  const returnFocus = useReturnFocus(drawerOpen);
  const lastPath = useRef(location.pathname);

  // Ao navegar, leva o foco ao conteúdo (leitores de tela anunciam a nova página).
  // Na carga inicial NÃO move o foco: o primeiro Tab precisa alcançar o link "Ir para o conteúdo".
  // Comparar com o caminho anterior (e não com um "primeiro render") também resiste ao double-invoke do StrictMode.
  useEffect(() => {
    if (lastPath.current === location.pathname) return;
    lastPath.current = location.pathname;
    document.getElementById('conteudo')?.focus({ preventScroll: true });
  }, [location.pathname]);
  return (
    <RealtimeProvider>
      <div className="min-h-screen lg:grid lg:grid-cols-[256px_minmax(0,1fr)]">
        <a href="#conteudo" className="sr-only z-[70] rounded-lg bg-accent px-4 py-2 text-accent-fg focus:not-sr-only focus:fixed focus:top-3 focus:left-3">
          Ir para o conteúdo
        </a>

        <aside className="sticky top-0 hidden h-screen border-r border-border bg-surface lg:block">
          <Sidebar />
        </aside>

        <Dialog.Root open={drawerOpen} onOpenChange={setDrawerOpen}>
          <Dialog.Portal>
            <Dialog.Overlay className="fixed inset-0 z-50 bg-black/50 lg:hidden" />
            <Dialog.Content onCloseAutoFocus={returnFocus} className="fixed inset-y-0 left-0 z-50 w-72 max-w-[85vw] border-r border-border bg-surface lg:hidden">
              <Dialog.Title className="sr-only">Menu de navegação</Dialog.Title>
              <Dialog.Description className="sr-only">Navegue entre as áreas do sistema</Dialog.Description>
              <Sidebar onNavigate={() => setDrawerOpen(false)} />
            </Dialog.Content>
          </Dialog.Portal>
        </Dialog.Root>

        <div className="flex min-w-0 flex-col">
          <Header onMenu={() => setDrawerOpen(true)} />
          <main id="conteudo" tabIndex={-1} className="mx-auto w-full max-w-[1400px] flex-1 p-4 outline-none md:p-6 lg:p-8">
            <Outlet />
          </main>
        </div>
      </div>
    </RealtimeProvider>
  );
}
