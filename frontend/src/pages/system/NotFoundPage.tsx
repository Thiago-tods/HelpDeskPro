import { StatusScreen } from './SystemPages';

export default function NotFoundPage() {
  return <StatusScreen code="404" title="Página não encontrada" description="O endereço não existe ou o conteúdo foi removido." />;
}
