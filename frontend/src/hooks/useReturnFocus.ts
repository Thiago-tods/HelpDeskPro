import { useCallback, useLayoutEffect, useRef } from 'react';

/**
 * O Radix Dialog só devolve o foco ao `Dialog.Trigger`. Nossos diálogos são controlados por estado (sem Trigger),
 * então guardamos o elemento focado no momento da abertura e o refocamos ao fechar (WCAG 2.4.3 — ordem do foco).
 *
 * O layout effect do componente pai roda antes do efeito que move o foco para dentro do diálogo,
 * por isso `document.activeElement` ainda é o elemento de origem.
 */
export function useReturnFocus(open: boolean): (event: Event) => void {
  const origin = useRef<HTMLElement | null>(null);

  useLayoutEffect(() => {
    if (open) origin.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }, [open]);

  return useCallback((event: Event) => {
    event.preventDefault();
    const target = origin.current;
    if (target && document.contains(target)) target.focus();
  }, []);
}
