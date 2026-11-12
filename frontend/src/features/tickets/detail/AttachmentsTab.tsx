import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Download, FileText, ImageIcon, Paperclip } from 'lucide-react';
import { useCurrentUser } from '@/app/auth';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { downloadFile, messageOf } from '@/lib/api';
import { formatBytes, formatDateTime } from '@/lib/format';
import type { Attachment, TicketDetail } from '@/types/api';
import { uploadAttachment, useAttachments } from '../api';

const ACCEPT = '.png,.jpg,.jpeg,.gif,.pdf,.txt,.csv,.docx,.xlsx';
const MAX_BYTES = 10 * 1024 * 1024; // o limite definitivo é o do servidor

function FileIcon({ type }: { type: string }) {
  return type.startsWith('image/') ? <ImageIcon className="size-5" aria-hidden /> : <FileText className="size-5" aria-hidden />;
}

export function AttachmentsTab({ ticket }: { ticket: TicketDetail }) {
  const user = useCurrentUser();
  const toast = useToast();
  const queryClient = useQueryClient();
  const list = useAttachments(ticket.id);
  const input = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [downloading, setDownloading] = useState<string | null>(null);

  const isFinal = ticket.status === 'CLOSED' || ticket.status === 'CANCELLED';
  const agentWithoutOwnership = user.role === 'AGENT' && ticket.assignedToId !== user.id;
  const canUpload = !isFinal && !agentWithoutOwnership;

  async function onFiles(files: FileList | null) {
    if (!files?.length) return;
    setUploading(true);
    for (const file of Array.from(files)) {
      if (file.size > MAX_BYTES) {
        toast.error(`"${file.name}" excede ${formatBytes(MAX_BYTES)}.`);
        continue;
      }
      try {
        await uploadAttachment(ticket.id, file);
        toast.success(`"${file.name}" anexado.`);
      } catch (error) {
        toast.error(`${file.name}: ${messageOf(error)}`);
      }
    }
    setUploading(false);
    if (input.current) input.current.value = '';
    await queryClient.invalidateQueries({ queryKey: ['attachments', ticket.id] });
  }

  async function download(a: Attachment) {
    setDownloading(a.id);
    try {
      await downloadFile(`/tickets/${ticket.id}/attachments/${a.id}`, a.fileName);
    } catch (error) {
      toast.error(messageOf(error));
    } finally {
      setDownloading(null);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      {canUpload ? (
        <div>
          <input ref={input} id="attachment-input" type="file" multiple accept={ACCEPT} className="sr-only" onChange={(e) => void onFiles(e.target.files)} />
          <Button variant="secondary" loading={uploading} onClick={() => input.current?.click()}>
            <Paperclip className="size-4" aria-hidden /> Anexar arquivos
          </Button>
          <p className="mt-1.5 text-xs text-muted">png, jpg, gif, pdf, txt, csv, docx ou xlsx — até 10 MB cada. O conteúdo é verificado no servidor.</p>
        </div>
      ) : (
        <p className="rounded-lg bg-surface-2 px-4 py-3 text-[13px] text-muted">
          {isFinal ? 'Chamado finalizado: não é possível anexar arquivos.' : 'Assuma o chamado para anexar arquivos.'}
        </p>
      )}

      {list.isPending ? (
        <SkeletonRows rows={2} />
      ) : list.isError ? (
        <ErrorState error={list.error} onRetry={() => void list.refetch()} />
      ) : list.data.length === 0 ? (
        <EmptyState icon={<Paperclip className="size-6" aria-hidden />} title="Sem anexos" description="Nenhum arquivo foi anexado a este chamado." />
      ) : (
        <ul className="divide-y divide-border rounded-xl border border-border">
          {list.data.map((a) => (
            <li key={a.id} className="flex items-center gap-3 px-4 py-3">
              <span className="text-muted"><FileIcon type={a.contentType} /></span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-[13px] font-medium">{a.fileName}</p>
                <p className="text-xs text-muted">{formatBytes(a.sizeBytes)} • {a.uploadedByName} • {formatDateTime(a.createdAt)}</p>
              </div>
              <Button variant="secondary" size="sm" loading={downloading === a.id} onClick={() => void download(a)} aria-label={`Baixar ${a.fileName}`}>
                <Download className="size-4" aria-hidden /> Baixar
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
