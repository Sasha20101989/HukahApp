import type { ReactNode } from "react";

type AsyncState = {
  isPending?: boolean;
  isLoading?: boolean;
  isFetching?: boolean;
  isError?: boolean;
  isSuccess?: boolean;
  error?: unknown;
};

export function getErrorMessage(error: unknown, fallback = "Операция не выполнена") {
  return error instanceof Error ? error.message : fallback;
}

export function FormError({ error, message, onRetry }: { error?: unknown; message?: string; onRetry?: () => void }) {
  const text = message ?? getErrorMessage(error, "Backend request failed");
  if (onRetry) {
    return <button className="notice inline" onClick={onRetry}>{text}</button>;
  }

  return <div className="notice inline" role="alert">{text}</div>;
}

export function FieldError({ message }: { message?: string }) {
  return message ? <div className="field-error" role="alert">{message}</div> : null;
}

export function EmptyState({ label, description, action }: { label: string; description?: string; action?: ReactNode }) {
  return <div className="empty-state"><strong>{label}</strong>{description && <span>{description}</span>}{action}</div>;
}

export function LoadingState({ label = "Загружаем..." }: { label?: string }) {
  return <div className="inline-state loading" aria-busy="true">{label}</div>;
}

export function MutationToast({ mutation, successMessage = "Сохранено", pendingMessage = "Выполняем...", errorMessage = "Операция не выполнена" }: { mutation: AsyncState; successMessage?: string; pendingMessage?: string; errorMessage?: string }) {
  if (mutation.isPending || mutation.isLoading) return <LoadingState label={pendingMessage} />;
  if (mutation.isError) return <FormError error={mutation.error} message={getErrorMessage(mutation.error, errorMessage)} />;
  if (mutation.isSuccess) return <div className="inline-state ok">{successMessage}</div>;
  return null;
}

export function QueryStateBlock({ state, empty, emptyLabel, loadingLabel, children }: { state: AsyncState; empty: boolean; emptyLabel: string; loadingLabel?: string; children: ReactNode }) {
  if (state.isLoading) return <LoadingState label={loadingLabel ?? "Загружаем данные..."} />;
  if (state.isError) return <FormError error={state.error} />;
  if (empty) return <EmptyState label={emptyLabel} />;
  return <>{children}</>;
}

export function FormField({ label, children }: { label: string; children: ReactNode }) {
  return <label className="field"><span>{label}</span>{children}</label>;
}

export function CrudToolbar({ title, children }: { title?: string; children: ReactNode }) {
  return <div className="form-strip">{title && <strong>{title}</strong>}{children}</div>;
}

export function CrudRowActions({ children }: { children: ReactNode }) {
  return <div className="form-actions">{children}</div>;
}

export function ActionButton({ children, confirm, danger = false, disabled = false, onClick, pending = false, pendingLabel = "Выполняем..." }: { children: ReactNode; confirm?: string; danger?: boolean; disabled?: boolean; onClick: () => void; pending?: boolean; pendingLabel?: string }) {
  const handleClick = () => {
    if (confirm && typeof window !== "undefined" && !window.confirm(confirm)) return;
    onClick();
  };

  return <button className={danger ? "danger" : undefined} disabled={disabled || pending} onClick={handleClick}>{pending ? pendingLabel : children}</button>;
}
