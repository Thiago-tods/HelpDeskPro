import type { ReactNode } from 'react';
import { LifeBuoy, Moon, Sun } from 'lucide-react';
import { useTheme } from '@/app/theme';
import { IconButton } from '@/components/ui/button';

/** Moldura das telas públicas (login, recuperação e redefinição de senha). */
export function AuthLayout({ title, description, children, footer }: { title: string; description?: string; children: ReactNode; footer?: ReactNode }) {
  const { theme, toggle } = useTheme();
  return (
    <div className="grid min-h-screen lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
      <section aria-hidden className="relative hidden flex-col justify-between bg-accent p-10 text-accent-fg lg:flex">
        <div className="flex items-center gap-3">
          <span className="grid size-10 place-items-center rounded-xl bg-white/15"><LifeBuoy className="size-6" /></span>
          <span className="text-lg font-semibold">HelpDesk Pro</span>
        </div>
        <div>
          <h2 className="max-w-md text-3xl leading-tight font-semibold">Suporte técnico organizado, do chamado à resolução.</h2>
          <p className="mt-4 max-w-md">Acompanhe prazos de SLA, converse com a equipe e tenha visibilidade de cada atendimento da Nexora Tecnologia.</p>
        </div>
        <p className="text-xs">Projeto de portfólio • dados fictícios</p>
      </section>

      <main className="relative grid place-items-center p-6">
        <div className="absolute top-4 right-4">
          <IconButton label={theme === 'dark' ? 'Mudar para o tema claro' : 'Mudar para o tema escuro'} onClick={toggle}>
            {theme === 'dark' ? <Sun className="size-5" aria-hidden /> : <Moon className="size-5" aria-hidden />}
          </IconButton>
        </div>
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-2.5 lg:hidden">
            <span className="grid size-9 place-items-center rounded-lg bg-accent text-accent-fg"><LifeBuoy className="size-5" aria-hidden /></span>
            <span className="text-lg font-semibold">HelpDesk Pro</span>
          </div>
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {description && <p className="mt-1.5 mb-6 text-[13px] text-muted">{description}</p>}
          {!description && <div className="mb-6" />}
          {children}
          {footer && <div className="mt-6 text-center text-[13px] text-muted">{footer}</div>}
        </div>
      </main>
    </div>
  );
}
