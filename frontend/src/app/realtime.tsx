import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { API_ORIGIN, refreshSession, tokenStore } from '@/lib/api';
import type { AppNotification, TicketEvent } from '@/types/api';
import { useCurrentUser } from './auth';
import { useToast } from '@/components/ui/toast';

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline';

// Fora do provider (ex.: testes) considera-se "conectado": nenhum polling de contingência.
const RealtimeContext = createContext<RealtimeStatus>('connected');

export const useRealtimeStatus = (): RealtimeStatus => useContext(RealtimeContext);

/** Sem tempo real, as telas voltam a consultar a API periodicamente (a aplicação segue funcional por HTTP). */
export function usePollingInterval(): number | false {
  return useRealtimeStatus() === 'connected' ? false : 30_000;
}

const RETRY_DELAYS = [2_000, 5_000, 10_000, 30_000];

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const user = useCurrentUser();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [status, setStatus] = useState<RealtimeStatus>('connecting');

  useEffect(() => {
    let disposed = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let attempt = 0;

    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`${API_ORIGIN}/hubs/tickets`, {
        accessTokenFactory: async () => {
          if (!tokenStore.get() || tokenStore.isExpiringSoon()) await refreshSession();
          return tokenStore.get() ?? '';
        },
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      // O estado de conexão é exposto na interface; o log do cliente só atrapalharia (ex.: StrictMode em desenvolvimento).
      .configureLogging(LogLevel.Critical)
      .build();

    // Os eventos trazem só ids: os dados são relidos via HTTP, onde a autorização por recurso é aplicada.
    connection.on('ticketEvent', (event: TicketEvent) => {
      void queryClient.invalidateQueries({ queryKey: ['tickets'] });
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      void queryClient.invalidateQueries({ queryKey: ['ticket', event.ticketId] });
      void queryClient.invalidateQueries({ queryKey: ['history', event.ticketId] });
      void queryClient.invalidateQueries({ queryKey: ['messages', event.ticketId] });
      void queryClient.invalidateQueries({ queryKey: ['attachments', event.ticketId] });
    });

    connection.on('notification', (notification: AppNotification) => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      void queryClient.invalidateQueries({ queryKey: ['unread-count'] });
      toast.info(notification.message);
    });

    connection.onreconnecting(() => setStatus('reconnecting'));
    connection.onreconnected(() => {
      setStatus('connected');
      void queryClient.invalidateQueries(); // recupera o que passou durante a queda
    });
    connection.onclose(() => {
      if (disposed) return;
      setStatus('offline');
      scheduleStart(); // ex.: token expirou (o servidor encerra) ou a API ficou indisponível
    });

    async function start() {
      if (disposed) return;
      try {
        await connection.start();
        attempt = 0;
        if (!disposed) {
          setStatus('connected');
          void queryClient.invalidateQueries();
        }
      } catch {
        if (!disposed) {
          setStatus('offline');
          scheduleStart();
        }
      }
    }

    function scheduleStart() {
      const delay = RETRY_DELAYS[Math.min(attempt, RETRY_DELAYS.length - 1)];
      attempt += 1;
      retryTimer = setTimeout(() => void start(), delay);
    }

    void start();

    return () => {
      disposed = true;
      clearTimeout(retryTimer);
      void connection.stop();
    };
  }, [user.id, queryClient, toast]);

  return <RealtimeContext.Provider value={status}>{children}</RealtimeContext.Provider>;
}
