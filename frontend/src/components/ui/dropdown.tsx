import type { ComponentProps } from 'react';
import * as Menu from '@radix-ui/react-dropdown-menu';
import { cn } from '@/lib/cn';

/** Menu não modal: não aplica aria-hidden ao resto da página (que continua focável), evitando violação de aria-hidden-focus. */
export function DropdownMenu(props: ComponentProps<typeof Menu.Root>) {
  return <Menu.Root modal={false} {...props} />;
}
export const DropdownTrigger = Menu.Trigger;

export function DropdownContent({ className, align = 'end', ...rest }: ComponentProps<typeof Menu.Content>) {
  return (
    <Menu.Portal>
      <Menu.Content
        align={align}
        sideOffset={8}
        className={cn('fade-in z-50 min-w-52 rounded-xl border border-border bg-surface p-1.5 shadow-xl', className)}
        {...rest}
      />
    </Menu.Portal>
  );
}

export function DropdownItem({ className, ...rest }: ComponentProps<typeof Menu.Item>) {
  return (
    <Menu.Item
      className={cn('flex cursor-pointer items-center gap-2.5 rounded-lg px-3 py-2 text-[13px] outline-none data-[highlighted]:bg-surface-2', className)}
      {...rest}
    />
  );
}

export function DropdownLabel({ className, ...rest }: ComponentProps<typeof Menu.Label>) {
  return <Menu.Label className={cn('px-3 py-1.5 text-xs text-muted', className)} {...rest} />;
}

export function DropdownSeparator() {
  return <Menu.Separator className="my-1.5 h-px bg-border" />;
}
