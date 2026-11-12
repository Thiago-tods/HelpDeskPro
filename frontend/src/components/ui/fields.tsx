import { useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react';
import { cn } from '@/lib/cn';

interface FieldShellProps {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  required?: boolean;
  children: ReactNode;
  className?: string;
  hideLabel?: boolean;
}

function FieldShell({ id, label, hint, error, required, children, className, hideLabel }: FieldShellProps) {
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <label htmlFor={id} className={cn('text-[13px] font-medium', hideLabel && 'sr-only')}>
        {label}
        {required && <span aria-hidden className="ml-0.5 text-danger">*</span>}
      </label>
      {children}
      {hint && !error && <p id={`${id}-hint`} className="text-xs text-muted">{hint}</p>}
      {error && <p id={`${id}-error`} role="alert" className="text-xs text-danger">{error}</p>}
    </div>
  );
}

const controlClass = (error?: string) =>
  cn(
    'w-full rounded-lg border bg-surface px-3 text-text placeholder:text-muted/80',
    'disabled:cursor-not-allowed disabled:bg-surface-2 disabled:opacity-70',
    error ? 'border-danger' : 'border-border hover:border-muted/60',
  );

const describedBy = (id: string, hint?: string, error?: string) =>
  error ? `${id}-error` : hint ? `${id}-hint` : undefined;

type Common = { label: string; hint?: string; error?: string; hideLabel?: boolean; wrapperClassName?: string };

export function TextField({ label, hint, error, hideLabel, wrapperClassName, className, required, ...rest }: Common & InputHTMLAttributes<HTMLInputElement>) {
  const id = useId();
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} required={required} className={wrapperClassName} hideLabel={hideLabel}>
      <input
        id={id}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        className={cn(controlClass(error), 'h-10', className)}
        {...rest}
      />
    </FieldShell>
  );
}

export function TextAreaField({ label, hint, error, hideLabel, wrapperClassName, className, required, rows = 4, ...rest }: Common & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const id = useId();
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} required={required} className={wrapperClassName} hideLabel={hideLabel}>
      <textarea
        id={id}
        rows={rows}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        className={cn(controlClass(error), 'resize-y py-2', className)}
        {...rest}
      />
    </FieldShell>
  );
}

export function SelectField({ label, hint, error, hideLabel, wrapperClassName, className, required, children, ...rest }: Common & SelectHTMLAttributes<HTMLSelectElement>) {
  const id = useId();
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} required={required} className={wrapperClassName} hideLabel={hideLabel}>
      <select
        id={id}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        className={cn(controlClass(error), 'h-10', className)}
        {...rest}
      >
        {children}
      </select>
    </FieldShell>
  );
}

export function CheckboxField({ label, hint, className, ...rest }: { label: string; hint?: string } & Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>) {
  const id = useId();
  return (
    <div className={cn('flex items-start gap-2.5', className)}>
      <input id={id} type="checkbox" className="mt-0.5 size-4 accent-[var(--accent)]" aria-describedby={hint ? `${id}-hint` : undefined} {...rest} />
      <label htmlFor={id} className="text-[13px]">
        {label}
        {hint && <span id={`${id}-hint`} className="block text-xs text-muted">{hint}</span>}
      </label>
    </div>
  );
}

/** Interruptor acessível (role="switch"). */
export function Switch({ checked, onChange, label, disabled }: { checked: boolean; onChange: (value: boolean) => void; label: string; disabled?: boolean }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:opacity-55',
        checked ? 'bg-accent' : 'bg-border',
      )}
    >
      <span className={cn('inline-block size-5 rounded-full bg-white shadow transition-transform', checked ? 'translate-x-5.5' : 'translate-x-0.5')} />
    </button>
  );
}
