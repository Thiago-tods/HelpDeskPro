import { describe, expect, it } from 'vitest';
import { navFor } from './nav';

const labels = (role: Parameters<typeof navFor>[0]) => navFor(role).flatMap((s) => s.items.map((i) => i.label));
const paths = (role: Parameters<typeof navFor>[0]) => navFor(role).flatMap((s) => s.items.map((i) => i.to));

describe('navFor (permissões visuais por papel)', () => {
  it('solicitante vê apenas as áreas de autoatendimento', () => {
    expect(labels('REQUESTER')).toEqual(['Início', 'Abrir chamado', 'Meus chamados', 'Notificações']);
    expect(paths('REQUESTER').some((p) => p.startsWith('/admin') || p === '/queue' || p === '/kanban')).toBe(false);
  });

  it('agente vê atendimento, mas não administração', () => {
    expect(labels('AGENT')).toEqual(expect.arrayContaining(['Fila', 'Atribuídos a mim', 'Kanban']));
    expect(paths('AGENT').some((p) => p.startsWith('/admin'))).toBe(false);
  });

  it('administrador vê atendimento e todas as telas administrativas', () => {
    expect(paths('ADMIN')).toEqual(expect.arrayContaining([
      '/queue', '/kanban', '/admin/users', '/admin/teams', '/admin/categories', '/admin/priorities',
      '/admin/sla', '/admin/reports', '/admin/audit', '/admin/settings',
    ]));
  });
});
