import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/button';

export function StatusScreen({ code, title, description, actions }: { code: string; title: string; description: string; actions?: ReactNode }) {
  return (
    <div role="main" className="grid min-h-[60vh] place-items-center px-4 text-center">
      <div className="max-w-md">
        <p className="text-6xl font-bold text-accent">{code}</p>
        <h1 className="mt-3 text-xl font-semibold">{title}</h1>
        <p className="mt-2 text-[13px] text-muted">{description}</p>
        <div className="mt-6 flex justify-center gap-2">
          {actions ?? (
            <Link to="/">
              <Button>Voltar ao início</Button>
            </Link>
          )}
        </div>
      </div>
    </div>
  );
}
