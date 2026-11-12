import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '@/app/auth';
import { PageLoader } from '@/components/ui/states';
import ForbiddenPage from '@/pages/system/ForbiddenPage';
import type { Role } from '@/types/api';

/** Exige sessão válida. A verificação real continua no backend; isto evita telas vazias e redireciona ao login. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const location = useLocation();

  if (status === 'loading') return <PageLoader label="Verificando sessão…" />;
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  return children;
}

/** Telas de login/recuperação: usuário já autenticado vai para o início. */
export function PublicOnly({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  if (status === 'loading') return <PageLoader label="Verificando sessão…" />;
  if (status === 'authenticated') return <Navigate to="/" replace />;
  return children;
}

/** Mostra a página 403 quando o papel não permite a rota (sem redirecionar, para manter a URL). */
export function RequireRole({ roles, children }: { roles: Role[]; children: ReactNode }) {
  const { user } = useAuth();
  if (!user || !roles.includes(user.role)) return <ForbiddenPage />;
  return children;
}
