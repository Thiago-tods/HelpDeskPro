import { describe, expect, it } from 'vitest';
import { formatBytes, formatMinutes, formatPercent, formatRelative, historyDescription, initials } from './format';

describe('formatMinutes', () => {
  it.each([
    [null, '—'],
    [0, '< 1min'],
    [15, '15min'],
    [60, '1h'],
    [90, '1h 30min'],
    [1440, '1d'],
    [2880, '2d'],
    [1500, '1d 1h'],
  ])('formata %s como %s', (input, expected) => expect(formatMinutes(input)).toBe(expected));
});

describe('formatBytes', () => {
  it('escolhe a unidade adequada', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(2048)).toBe('2.0 KB');
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
  });
});

describe('formatPercent / initials', () => {
  it('trata nulo e usa vírgula decimal', () => {
    expect(formatPercent(null)).toBe('—');
    expect(formatPercent(87.5)).toBe('87,5%');
  });
  it('gera até duas iniciais', () => {
    expect(initials('Ana Beatriz Lima')).toBe('AB');
    expect(initials('Rafael')).toBe('R');
  });
});

describe('formatRelative', () => {
  const now = new Date('2026-01-10T12:00:00Z').getTime();
  it('descreve intervalos recentes em português', () => {
    expect(formatRelative('2026-01-10T11:55:00Z', now)).toContain('5 minutos');
    expect(formatRelative('2026-01-10T09:00:00Z', now)).toContain('3 horas');
  });
});

describe('historyDescription', () => {
  it('traduz mudanças de status', () => {
    expect(historyDescription({ action: 'STATUS_CHANGED', oldValue: 'NEW', newValue: 'IN_PROGRESS' })).toBe('Status alterado: Novo → Em atendimento');
  });
  it('descreve atribuição e violação de SLA', () => {
    expect(historyDescription({ action: 'ASSIGNED', oldValue: null, newValue: 'Camila Torres' })).toBe('Atribuído a Camila Torres');
    expect(historyDescription({ action: 'SLA_BREACHED', oldValue: null, newValue: 'FIRST_RESPONSE' })).toContain('primeira resposta');
  });
});
