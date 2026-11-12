import { useEffect, useRef, useState, type HTMLAttributes, type TdHTMLAttributes, type ThHTMLAttributes } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { cn } from '@/lib/cn';
import { Button } from './button';

/**
 * Contêiner de tabela com rolagem horizontal. Quando o conteúdo realmente transborda, vira uma região focável
 * (WCAG 2.1.1): quem navega por teclado consegue rolar com as setas. O nome vem do <caption> da tabela.
 */
export function TableWrap({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  const ref = useRef<HTMLDivElement>(null);
  const [scrollable, setScrollable] = useState(false);
  const [label, setLabel] = useState('Tabela');

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const check = () => {
      setScrollable(el.scrollWidth > el.clientWidth + 1);
      setLabel(el.querySelector('caption')?.textContent?.trim() || 'Tabela');
    };
    check();
    const observer = new ResizeObserver(check);
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    <div
      ref={ref}
      className={cn('overflow-x-auto', className)}
      {...(scrollable ? { role: 'region', tabIndex: 0, 'aria-label': label } : {})}
      {...rest}
    />
  );
}

export function Table({ className, ...rest }: HTMLAttributes<HTMLTableElement>) {
  return <table className={cn('w-full border-collapse text-left text-[13px]', className)} {...rest} />;
}

export function Th({ className, ...rest }: ThHTMLAttributes<HTMLTableCellElement>) {
  return (
    <th
      scope="col"
      className={cn('border-b border-border bg-surface-2/60 px-4 py-2.5 text-xs font-semibold tracking-wide whitespace-nowrap text-muted uppercase', className)}
      {...rest}
    />
  );
}

export function Td({ className, ...rest }: TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={cn('border-b border-border px-4 py-3 align-middle', className)} {...rest} />;
}

export function Tr({ className, ...rest }: HTMLAttributes<HTMLTableRowElement>) {
  return <tr className={cn('transition-colors hover:bg-surface-2/50', className)} {...rest} />;
}

interface PaginationProps {
  page: number;
  totalPages: number;
  totalCount: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}

export function Pagination({ page, totalPages, totalCount, pageSize, onPageChange }: PaginationProps) {
  if (totalCount === 0) return null;
  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <nav aria-label="Paginação" className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3">
      <p className="text-[13px] text-muted" aria-live="polite">
        {from}–{to} de {totalCount}
      </p>
      <div className="flex items-center gap-2">
        <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)} aria-label="Página anterior">
          <ChevronLeft className="size-4" aria-hidden /> Anterior
        </Button>
        <span className="text-[13px]">
          Página {page} de {Math.max(totalPages, 1)}
        </span>
        <Button variant="secondary" size="sm" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)} aria-label="Próxima página">
          Próxima <ChevronRight className="size-4" aria-hidden />
        </Button>
      </div>
    </nav>
  );
}
