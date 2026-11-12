import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, refreshSession, tokenStore } from '@/lib/api';
import type { AuthSession, Role, User } from '@/types/api';

type Status = 'loading' | 'authenticated' | 'anonymous';

interface AuthContextValue {
  status: Status;
  user: User | null;
  login: (email: string, password: string) => Promise<User>;
  logout: () => Promise<void>;
  /** Atualiza sessão/usuário após troca de senha ou edição de perfil. */
  applySession: (session: AuthSession) => void;
  setUser: (user: User) => void;
  hasRole: (...roles: Role[]) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUserState] = useState<User | null>(null);

  const clear = useCallback(() => {
    tokenStore.set(null);
    setUserState(null);
    setStatus('anonymous');
    queryClient.clear(); // nenhum dado de um usuário sobrevive ao logout
  }, [queryClient]);

  // Restaura a sessão no carregamento usando o cookie HttpOnly de refresh.
  useEffect(() => {
    tokenStore.onExpired(clear);
    let cancelled = false;
    void refreshSession().then((session) => {
      if (cancelled) return;
      if (session) {
        setUserState(session.user);
        setStatus('authenticated');
      } else {
        setStatus('anonymous');
      }
    });
    return () => {
      cancelled = true;
      tokenStore.onExpired(null);
    };
  }, [clear]);

  const applySession = useCallback((session: AuthSession) => {
    tokenStore.set(session);
    setUserState(session.user);
    setStatus('authenticated');
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      const session = await api<AuthSession>('/auth/login', { method: 'POST', body: { email, password }, auth: false });
      queryClient.clear();
      applySession(session);
      return session.user;
    },
    [applySession, queryClient],
  );

  const logout = useCallback(async () => {
    try {
      await api('/auth/logout', { method: 'POST', auth: false });
    } catch {
      // mesmo sem resposta do servidor, a sessão local é encerrada
    }
    clear();
  }, [clear]);

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      login,
      logout,
      applySession,
      setUser: setUserState,
      hasRole: (...roles) => user !== null && roles.includes(user.role),
    }),
    [status, user, login, logout, applySession],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth deve ser usado dentro de AuthProvider');
  return ctx;
}

/** Usuário autenticado garantido (use dentro de rotas protegidas). */
export function useCurrentUser(): User {
  const { user } = useAuth();
  if (!user) throw new Error('Usuário não autenticado');
  return user;
}
