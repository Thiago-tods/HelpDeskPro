import { z } from 'zod';

/** Espelha a política do servidor (Identity): a validação definitiva é sempre a do backend. */
export const passwordSchema = z
  .string()
  .min(10, 'Use ao menos 10 caracteres.')
  .max(128, 'Use no máximo 128 caracteres.')
  .regex(/[a-z]/, 'Inclua uma letra minúscula.')
  .regex(/[A-Z]/, 'Inclua uma letra maiúscula.')
  .regex(/\d/, 'Inclua um número.')
  .regex(/[^A-Za-z0-9]/, 'Inclua um símbolo.');
