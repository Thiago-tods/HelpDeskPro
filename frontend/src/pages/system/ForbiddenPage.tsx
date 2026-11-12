import { StatusScreen } from './SystemPages';

export default function ForbiddenPage() {
  return (
    <StatusScreen
      code="403"
      title="Acesso negado"
      description="Seu perfil não tem permissão para acessar esta área. Se você acredita que isso é um engano, fale com um administrador."
    />
  );
}
