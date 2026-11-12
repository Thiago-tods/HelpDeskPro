import { useState } from 'react';
import { Download } from 'lucide-react';
import { PageHeader } from '@/components/common';
import { Button } from '@/components/ui/button';
import { Card, CardHeader } from '@/components/ui/card';
import { SelectField } from '@/components/ui/fields';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { useAgentReport, useSlaReport } from '@/features/insights/api';
import { downloadFile, messageOf } from '@/lib/api';
import { formatMinutes, formatPercent } from '@/lib/format';

const periodStart = (days: number) => new Date(Date.now() - days * 86_400_000).toISOString();

export default function ReportsPage() {
  const toast = useToast();
  const [days, setDays] = useState(30);
  // A data inicial é fixada ao escolher o período (evita nova consulta a cada renderização).
  const [from, setFrom] = useState(() => periodStart(30));
  const [exporting, setExporting] = useState(false);
  const sla = useSlaReport({ from });
  const agents = useAgentReport({ from });

  async function exportCsv() {
    setExporting(true);
    try {
      await downloadFile('/reports/tickets/export', 'chamados.csv', { from });
      toast.success('Exportação concluída.');
    } catch (error) {
      toast.error(messageOf(error));
    } finally {
      setExporting(false);
    }
  }

  return (
    <>
      <PageHeader
        title="Relatórios"
        description="Desempenho de SLA e produtividade da equipe."
        actions={
          <>
            <SelectField label="Período" hideLabel value={String(days)} onChange={(e) => { const d = Number(e.target.value); setDays(d); setFrom(periodStart(d)); }} className="w-44">
              <option value="7">Últimos 7 dias</option>
              <option value="30">Últimos 30 dias</option>
              <option value="90">Últimos 90 dias</option>
              <option value="365">Último ano</option>
            </SelectField>
            <Button variant="secondary" loading={exporting} onClick={() => void exportCsv()}>
              <Download className="size-4" aria-hidden /> Exportar CSV
            </Button>
          </>
        }
      />

      <div className="flex flex-col gap-6">
        <Card>
          <CardHeader
            title="Conformidade de SLA por prioridade"
            description={sla.data ? `Conformidade geral: ${formatPercent(sla.data.overallComplianceRate)} (chamados sem violação).` : undefined}
          />
          {sla.isPending ? <SkeletonRows rows={4} /> : sla.isError ? <ErrorState error={sla.error} onRetry={() => void sla.refetch()} /> : sla.data.rows.length === 0 ? (
            <EmptyState title="Sem chamados no período" />
          ) : (
            <TableWrap>
              <Table>
                <caption className="sr-only">Conformidade de SLA por prioridade</caption>
                <thead>
                  <tr><Th>Prioridade</Th><Th>Chamados</Th><Th>1ª resposta (ok / violada)</Th><Th>Resolução (ok / violada)</Th><Th>Tempo médio 1ª resposta</Th><Th>Tempo médio resolução</Th><Th>Conformidade</Th></tr>
                </thead>
                <tbody>
                  {sla.data.rows.map((r) => (
                    <Tr key={r.priorityId}>
                      <Td className="font-medium">{r.priorityName}</Td>
                      <Td>{r.total}</Td>
                      <Td>{r.firstResponseMet} / <span className={r.firstResponseMissed ? 'font-semibold text-danger' : ''}>{r.firstResponseMissed}</span></Td>
                      <Td>{r.resolutionMet} / <span className={r.resolutionMissed ? 'font-semibold text-danger' : ''}>{r.resolutionMissed}</span></Td>
                      <Td>{formatMinutes(r.avgFirstResponseMinutes)}</Td>
                      <Td>{formatMinutes(r.avgResolutionMinutes)}</Td>
                      <Td className="font-semibold">{formatPercent(r.complianceRate)}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>
          )}
        </Card>

        <Card>
          <CardHeader title="Produtividade por agente" />
          {agents.isPending ? <SkeletonRows rows={3} /> : agents.isError ? <ErrorState error={agents.error} onRetry={() => void agents.refetch()} /> : agents.data.rows.length === 0 ? (
            <EmptyState title="Sem atribuições no período" />
          ) : (
            <TableWrap>
              <Table>
                <caption className="sr-only">Produtividade por agente</caption>
                <thead><tr><Th>Agente</Th><Th>Atribuídos</Th><Th>Ativos</Th><Th>Resolvidos</Th><Th>Atrasados</Th><Th>Tempo médio de resolução</Th></tr></thead>
                <tbody>
                  {agents.data.rows.map((r) => (
                    <Tr key={r.agentId}>
                      <Td className="font-medium">{r.agentName}</Td>
                      <Td>{r.assigned}</Td>
                      <Td>{r.active}</Td>
                      <Td>{r.resolved}</Td>
                      <Td className={r.overdue ? 'font-semibold text-danger' : ''}>{r.overdue}</Td>
                      <Td>{formatMinutes(r.avgResolutionMinutes)}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>
          )}
        </Card>
      </div>
    </>
  );
}
