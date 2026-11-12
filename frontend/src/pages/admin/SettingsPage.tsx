import type { ReactNode } from 'react';
import { Moon, Sun } from 'lucide-react';
import { useTheme } from '@/app/theme';
import { DefinitionList, PageHeader } from '@/components/common';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardBody, CardHeader } from '@/components/ui/card';
import { ErrorState, SkeletonRows } from '@/components/ui/states';
import { useSettings } from '@/features/insights/api';
import { formatBytes } from '@/lib/format';

function Section({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader title={title} description={description} />
      <CardBody>{children}</CardBody>
    </Card>
  );
}

export default function SettingsPage() {
  const settings = useSettings();
  const { theme, toggle } = useTheme();
  const s = settings.data;

  return (
    <>
      <PageHeader
        title="Configurações"
        description="Parâmetros de execução do sistema. A edição é feita por User Secrets ou variáveis de ambiente; segredos nunca são exibidos."
        actions={s && <Badge tone="info">Ambiente: {s.environment}</Badge>}
      />

      <div className="grid gap-6 lg:grid-cols-2">
        <Section title="Aparência" description="Preferência salva apenas neste navegador.">
          <Button variant="secondary" onClick={toggle} aria-pressed={theme === 'dark'}>
            {theme === 'dark' ? <Sun className="size-4" aria-hidden /> : <Moon className="size-4" aria-hidden />}
            Tema {theme === 'dark' ? 'escuro' : 'claro'} — alternar
          </Button>
        </Section>

        {settings.isPending ? (
          <Card className="lg:col-span-1"><SkeletonRows rows={4} /></Card>
        ) : settings.isError ? (
          <Card><ErrorState error={settings.error} onRetry={() => void settings.refetch()} /></Card>
        ) : (
          <>
            <Section title="SLA" description="Alterar prazos: menu SLA / Prioridades.">
              <DefinitionList items={[
                { label: 'Calendário', value: s!.sla.calendar },
                { label: 'Aviso de proximidade', value: `${s!.sla.warningPercent}% do prazo` },
                { label: 'Conferência automática', value: s!.sla.checkIntervalSeconds > 0 ? `a cada ${s!.sla.checkIntervalSeconds}s` : 'desativada' },
              ]} />
            </Section>

            <Section title="Autenticação e segurança">
              <DefinitionList items={[
                { label: 'Validade do acesso', value: `${s!.auth.accessTokenMinutes} minutos` },
                { label: 'Validade da sessão (refresh)', value: `${s!.auth.refreshTokenDays} dias` },
                { label: 'Tamanho mínimo da senha', value: `${s!.auth.passwordMinLength} caracteres` },
                { label: 'Bloqueio por tentativas', value: `após ${s!.auth.lockoutMaxFailures} falhas` },
                { label: 'Limite de login', value: `${s!.authRateLimitPerMinute} requisições/min por IP` },
                { label: 'Origens permitidas (CORS)', value: s!.corsAllowedOrigins.join(', ') },
              ]} />
            </Section>

            <Section title="Anexos">
              <DefinitionList items={[
                { label: 'Tamanho máximo', value: formatBytes(s!.attachments.maxBytes) },
                { label: 'Máximo por chamado', value: `${s!.attachments.maxPerTicket} arquivos` },
                { label: 'Extensões permitidas', value: s!.attachments.allowedExtensions.join(' ') },
              ]} />
            </Section>
          </>
        )}
      </div>
    </>
  );
}
