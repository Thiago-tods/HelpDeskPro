import { isRouteErrorResponse, Link, useRouteError } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { StatusScreen } from './SystemPages';

/** Erro inesperado de renderização/carregamento. Não exibe detalhes técnicos ao usuário. */
export default function RouteError() {
  const error = useRouteError();

  if (isRouteErrorResponse(error) && error.status === 404) {
    return <StatusScreen code="404" title="Página não encontrada" description="O endereço não existe ou o conteúdo foi removido." />;
  }

  return (
    <StatusScreen
      code="Ops"
      title="Algo deu errado"
      description="Ocorreu um erro inesperado. Tente recarregar a página; se persistir, avise o suporte."
      actions={
        <>
          <Button onClick={() => window.location.reload()}>Recarregar</Button>
          <Link to="/">
            <Button variant="secondary">Ir ao início</Button>
          </Link>
        </>
      }
    />
  );
}
