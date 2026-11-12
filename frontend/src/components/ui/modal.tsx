import { useState, type ReactNode } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { useReturnFocus } from '@/hooks/useReturnFocus';
import { cn } from '@/lib/cn';
import { Button } from './button';

interface ModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  size?: 'sm' | 'md' | 'lg';
  children: ReactNode;
}

const sizes = { sm: 'max-w-md', md: 'max-w-xl', lg: 'max-w-3xl' };

/** Modal acessível (Radix Dialog): foco preso, Esc fecha, título e descrição associados. */
export function Modal({ open, onOpenChange, title, description, size = 'md', children }: ModalProps) {
  const returnFocus = useReturnFocus(open);
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/50" />
        <Dialog.Content
          onCloseAutoFocus={returnFocus}
          className={cn(
            'fade-in fixed top-1/2 left-1/2 z-50 max-h-[90vh] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-xl border border-border bg-surface shadow-xl',
            sizes[size],
          )}
        >
          <div className="flex items-start justify-between gap-4 border-b border-border px-5 py-4">
            <div>
              <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
              {description ? (
                <Dialog.Description className="mt-0.5 text-[13px] text-muted">{description}</Dialog.Description>
              ) : (
                <Dialog.Description className="sr-only">{title}</Dialog.Description>
              )}
            </div>
            <Dialog.Close aria-label="Fechar" className="rounded-lg p-1.5 text-muted hover:bg-surface-2">
              <X className="size-5" aria-hidden />
            </Dialog.Close>
          </div>
          <div className="p-5">{children}</div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function ModalFooter({ children }: { children: ReactNode }) {
  return <div className="mt-5 flex flex-wrap justify-end gap-2">{children}</div>;
}

interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel?: string;
  tone?: 'primary' | 'danger';
  onConfirm: () => Promise<unknown> | void;
}

/** Confirmação obrigatória para ações destrutivas ou irreversíveis. */
export function ConfirmDialog({ open, onOpenChange, title, description, confirmLabel = 'Confirmar', tone = 'danger', onConfirm }: ConfirmDialogProps) {
  const [busy, setBusy] = useState(false);

  async function handleConfirm() {
    setBusy(true);
    try {
      await onConfirm();
      onOpenChange(false);
    } catch {
      // o chamador exibe o erro (toast); o diálogo permanece aberto para nova tentativa
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal open={open} onOpenChange={onOpenChange} title={title} description={description} size="sm">
      <ModalFooter>
        <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={busy}>
          Voltar
        </Button>
        <Button variant={tone} loading={busy} onClick={() => void handleConfirm()}>
          {confirmLabel}
        </Button>
      </ModalFooter>
    </Modal>
  );
}
