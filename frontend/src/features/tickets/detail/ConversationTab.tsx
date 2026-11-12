import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Lock, Send } from 'lucide-react';
import { useCurrentUser } from '@/app/auth';
import { Avatar, RoleBadge } from '@/components/common';
import { Button } from '@/components/ui/button';
import { CheckboxField, TextAreaField } from '@/components/ui/fields';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { cn } from '@/lib/cn';
import { formatDateTime } from '@/lib/format';
import type { TicketDetail } from '@/types/api';
import { useMessages, usePostMessage } from '../api';

export function ConversationTab({ ticket }: { ticket: TicketDetail }) {
  const user = useCurrentUser();
  const messages = useMessages(ticket.id);
  const post = usePostMessage(ticket.id);
  const [content, setContent] = useState('');
  const [internal, setInternal] = useState(false);
  const endRef = useRef<HTMLDivElement>(null);

  const isStaff = user.role !== 'REQUESTER';
  const isFinal = ticket.status === 'CLOSED' || ticket.status === 'CANCELLED';
  // Conveniência visual: o servidor exige que o agente seja o responsável para respostas públicas.
  const mustStayInternal = user.role === 'AGENT' && ticket.assignedToId !== user.id;
  const asInternal = isStaff && (internal || mustStayInternal);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'nearest' });
  }, [messages.data?.length]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const text = content.trim();
    if (!text) return;
    try {
      await post.mutateAsync({ content: text, isInternal: asInternal });
      setContent('');
    } catch {
      // toast global exibido
    }
  }

  return (
    <div className="flex flex-col gap-5">
      <div role="log" aria-label="Mensagens do chamado" tabIndex={0} className="flex max-h-[28rem] flex-col gap-4 overflow-y-auto pr-1">
        {messages.isPending ? (
          <SkeletonRows rows={3} />
        ) : messages.isError ? (
          <ErrorState error={messages.error} onRetry={() => void messages.refetch()} />
        ) : messages.data.length === 0 ? (
          <EmptyState title="Nenhuma mensagem ainda" description="Use o campo abaixo para iniciar a conversa." />
        ) : (
          messages.data.map((m) => {
            const mine = m.authorId === user.id;
            return (
              <article key={m.id} aria-label={`${m.isInternal ? 'Nota interna' : 'Mensagem'} de ${m.authorName}`} className={cn('flex gap-3', mine && 'flex-row-reverse')}>
                <Avatar name={m.authorName} />
                <div className={cn('max-w-[85%] min-w-0 rounded-xl border px-4 py-3', m.isInternal ? 'border-warning/40 bg-warning-soft' : mine ? 'border-accent/30 bg-accent-soft' : 'border-border bg-surface-2')}>
                  <header className="mb-1 flex flex-wrap items-center gap-2 text-xs">
                    <strong className="text-[13px]">{m.authorName}</strong>
                    {m.authorRole !== 'REQUESTER' && <RoleBadge role={m.authorRole} />}
                    <time dateTime={m.createdAt} className="text-muted">{formatDateTime(m.createdAt)}</time>
                  </header>
                  {m.isInternal && (
                    <p className="mb-1 flex items-center gap-1 text-xs font-semibold text-warning">
                      <Lock className="size-3" aria-hidden /> Nota interna — visível só para a equipe
                    </p>
                  )}
                  <p className="text-[13px] break-words whitespace-pre-wrap">{m.content}</p>
                </div>
              </article>
            );
          })
        )}
        <div ref={endRef} />
      </div>

      {isFinal ? (
        <p className="rounded-lg bg-surface-2 px-4 py-3 text-[13px] text-muted">Este chamado foi finalizado e não aceita novas mensagens.</p>
      ) : (
        <form onSubmit={submit} className="flex flex-col gap-3 border-t border-border pt-4">
          <TextAreaField
            label={asInternal ? 'Nova nota interna' : 'Nova mensagem'}
            rows={3}
            value={content}
            onChange={(e) => setContent(e.target.value)}
            placeholder={asInternal ? 'Visível apenas para agentes e administradores…' : 'Escreva sua mensagem…'}
            maxLength={10000}
            className={asInternal ? 'border-warning/60 bg-warning-soft/40' : undefined}
          />
          <div className="flex flex-wrap items-center justify-between gap-3">
            {isStaff ? (
              <CheckboxField
                label="Nota interna"
                hint={mustStayInternal ? 'Assuma o chamado para responder ao solicitante.' : 'Não será exibida ao solicitante.'}
                checked={asInternal}
                disabled={mustStayInternal}
                onChange={(e) => setInternal(e.target.checked)}
              />
            ) : (
              <span />
            )}
            <Button type="submit" loading={post.isPending} disabled={!content.trim()}>
              <Send className="size-4" aria-hidden /> {asInternal ? 'Salvar nota' : 'Enviar'}
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}
