import { Area, AreaChart, Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import type { DashboardSummary, NamedCount } from '@/types/api';

const axis = { fill: 'var(--muted)', fontSize: 12 };
const tooltipStyle = {
  background: 'var(--surface)',
  border: '1px solid var(--border)',
  borderRadius: 8,
  color: 'var(--text)',
  fontSize: 13,
};

const shortDate = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`;

/** Tabela equivalente, visível apenas para leitores de tela. */
function ChartTable({ caption, headers, rows }: { caption: string; headers: string[]; rows: (string | number)[][] }) {
  return (
    <table className="sr-only">
      <caption>{caption}</caption>
      <thead>
        <tr>{headers.map((h) => <th key={h} scope="col">{h}</th>)}</tr>
      </thead>
      <tbody>
        {rows.map((row, i) => <tr key={i}>{row.map((cell, j) => <td key={j}>{cell}</td>)}</tr>)}
      </tbody>
    </table>
  );
}

export function TimelineChart({ data }: { data: DashboardSummary['timeline'] }) {
  const created = data.reduce((sum, p) => sum + p.created, 0);
  const resolved = data.reduce((sum, p) => sum + p.resolved, 0);
  const label = `Evolução diária: ${created} chamados criados e ${resolved} resolvidos em ${data.length} dias.`;

  return (
    <div>
      <div role="img" aria-label={label} className="h-64 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={data} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
            <CartesianGrid stroke="var(--chart-grid)" vertical={false} />
            <XAxis dataKey="date" tickFormatter={shortDate} tick={axis} tickLine={false} axisLine={false} minTickGap={24} />
            <YAxis allowDecimals={false} tick={axis} tickLine={false} axisLine={false} />
            <Tooltip contentStyle={tooltipStyle} labelFormatter={(v) => shortDate(String(v))} />
            <Legend wrapperStyle={{ fontSize: 13 }} />
            <Area type="monotone" dataKey="created" name="Criados" stroke="var(--chart-1)" fill="var(--chart-1)" fillOpacity={0.18} strokeWidth={2} />
            <Area type="monotone" dataKey="resolved" name="Resolvidos" stroke="var(--chart-2)" fill="var(--chart-2)" fillOpacity={0.18} strokeWidth={2} />
          </AreaChart>
        </ResponsiveContainer>
      </div>
      <ChartTable caption="Chamados por dia" headers={['Dia', 'Criados', 'Resolvidos']} rows={data.map((p) => [p.date, p.created, p.resolved])} />
    </div>
  );
}

export function BarsChart({ data, title, color = 'var(--chart-1)' }: { data: NamedCount[]; title: string; color?: string }) {
  const height = Math.max(160, data.length * 38 + 24);
  return (
    <div>
      <div role="img" aria-label={`${title}: ${data.map((d) => `${d.name} ${d.count}`).join(', ') || 'sem dados'}`} style={{ height }} className="w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data} layout="vertical" margin={{ top: 0, right: 16, left: 8, bottom: 0 }}>
            <CartesianGrid stroke="var(--chart-grid)" horizontal={false} />
            <XAxis type="number" allowDecimals={false} tick={axis} tickLine={false} axisLine={false} />
            <YAxis type="category" dataKey="name" width={110} tick={axis} tickLine={false} axisLine={false} />
            <Tooltip contentStyle={tooltipStyle} cursor={{ fill: 'var(--surface-2)' }} />
            <Bar dataKey="count" name="Chamados" fill={color} radius={[0, 6, 6, 0]} barSize={20} />
          </BarChart>
        </ResponsiveContainer>
      </div>
      <ChartTable caption={title} headers={['Item', 'Chamados']} rows={data.map((d) => [d.name, d.count])} />
    </div>
  );
}
