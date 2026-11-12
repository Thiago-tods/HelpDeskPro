import type { HTMLAttributes, ReactNode } from 'react';
import { cn } from '@/lib/cn';

export function Card({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  // min-w-0: itens de grid não podem crescer além da coluna por causa de tabelas internas (que rolam por conta própria).
  return <div className={cn('min-w-0 rounded-xl border border-border bg-surface', className)} {...rest} />;
}

export function CardHeader({ title, description, actions, as: Heading = 'h2' }: { title: string; description?: string; actions?: ReactNode; as?: 'h2' | 'h3' }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-5 py-4">
      <div>
        <Heading className="text-[15px] font-semibold">{title}</Heading>
        {description && <p className="mt-0.5 text-[13px] text-muted">{description}</p>}
      </div>
      {actions}
    </div>
  );
}

export function CardBody({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('p-5', className)} {...rest} />;
}
