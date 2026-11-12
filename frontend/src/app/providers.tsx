import { useState, type ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { ToastProvider, useToast } from '@/components/ui/toast';
import { ApiError, messageOf } from '@/lib/api';
import { createQueryClient } from '@/lib/query';
import { AuthProvider } from './auth';
import { ThemeProvider } from './theme';

function QueryProvider({ children }: { children: ReactNode }) {
  const toast = useToast();
  const [client] = useState(() =>
    createQueryClient((error) => {
      // Erros de validação (400) e regras de negócio (422) viram toast; 401 já é tratado pela renovação de sessão.
      if (error instanceof ApiError && error.status === 401) return;
      toast.error(messageOf(error));
    }),
  );
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <ToastProvider>
        <QueryProvider>
          <AuthProvider>{children}</AuthProvider>
        </QueryProvider>
      </ToastProvider>
    </ThemeProvider>
  );
}
