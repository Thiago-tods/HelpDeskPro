import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { TextField } from '@/components/ui/fields';
import { ApiError, api } from '@/lib/api';
import { AuthLayout } from '@/layouts/AuthLayout';

const schema = z.object({ email: z.string().min(1, 'Informe seu e-mail.').pipe(z.email('Informe um e-mail válido.')) });
type FormValues = z.infer<typeof schema>;

interface ForgotResponse {
  message: string;
  /** Só presente no modo demonstrativo (ambiente de desenvolvimento, sem envio de e-mail). */
  resetToken: string | null;
}

export default function ForgotPasswordPage() {
  const [result, setResult] = useState<{ email: string; response: ForgotResponse } | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '' } });

  async function onSubmit({ email }: FormValues) {
    setFormError(null);
    try {
      const response = await api<ForgotResponse>('/auth/forgot-password', { method: 'POST', body: { email: email.trim() }, auth: false });
      setResult({ email: email.trim(), response });
    } catch (error) {
      setFormError(error instanceof ApiError && error.status === 429 ? 'Muitas tentativas. Aguarde um minuto.' : 'Não foi possível processar o pedido agora.');
    }
  }

  const resetLink = result?.response.resetToken
    ? `/reset-password?email=${encodeURIComponent(result.email)}&token=${encodeURIComponent(result.response.resetToken)}`
    : null;

  return (
    <AuthLayout
      title="Recuperar senha"
      description="Informe o e-mail da conta para gerar a redefinição."
      footer={<Link to="/login" className="font-medium text-accent hover:underline">Voltar ao login</Link>}
    >
      {result ? (
        <div className="flex flex-col gap-4" role="status">
          <p className="rounded-lg border border-success/40 bg-success-soft px-3.5 py-2.5 text-[13px] text-success">{result.response.message}</p>
          {resetLink ? (
            <div className="rounded-lg border border-warning/40 bg-warning-soft px-3.5 py-3 text-[13px] text-warning">
              <p className="font-semibold">Modo demonstrativo</p>
              <p className="mt-1">Este ambiente não envia e-mails. Use o link abaixo para definir a nova senha.</p>
              <Link to={resetLink} className="mt-2 inline-block font-semibold underline">Redefinir senha agora</Link>
            </div>
          ) : (
            <p className="text-[13px] text-muted">Se o e-mail estiver cadastrado, você receberá as instruções.</p>
          )}
        </div>
      ) : (
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          {formError && <div role="alert" className="rounded-lg border border-danger/40 bg-danger-soft px-3.5 py-2.5 text-[13px] text-danger">{formError}</div>}
          <TextField label="E-mail" type="email" autoComplete="username" autoFocus error={errors.email?.message} {...register('email')} />
          <Button type="submit" loading={isSubmitting} className="w-full">Gerar redefinição</Button>
        </form>
      )}
    </AuthLayout>
  );
}
