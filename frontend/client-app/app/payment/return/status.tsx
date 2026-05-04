"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { getPaymentStatus } from "../../../lib/api";
import { FormError, LoadingState } from "../../../lib/ui";

const terminalStatuses = new Set(["SUCCESS", "FAILED", "REFUNDED", "PARTIALLY_REFUNDED"]);

export function PaymentReturnStatus() {
  const params = useSearchParams();
  const paymentId = params.get("paymentId") ?? "";
  const fallbackStatus = params.get("status") ?? "processing";
  const bookingId = params.get("bookingId");
  const payment = useQuery({
    queryKey: ["payment-status", paymentId],
    enabled: Boolean(paymentId),
    queryFn: () => getPaymentStatus(paymentId),
    refetchInterval: (query) => terminalStatuses.has(query.state.data?.status ?? "") ? false : 3000,
    retry: 3
  });

  const status = normalizeStatus(payment.data?.status ?? fallbackStatus);
  const isSuccess = status === "SUCCESS";
  const isFailed = status === "FAILED";
  const isPending = !terminalStatuses.has(status);
  const resolvedBookingId = payment.data?.bookingId ?? bookingId;

  return (
    <main className="booking-shell compact">
      <section className="panel success-step">
        <h1>Статус оплаты: {status}</h1>
        <p>{statusMessage(status, payment.isFetching)}</p>
        <div className="summary-line"><span>Payment</span><strong>{paymentId || "не передан"}</strong></div>
        <div className="summary-line"><span>Booking</span><strong>{resolvedBookingId ?? "не передан"}</strong></div>
        {payment.data && <div className="summary-line"><span>Сумма</span><strong>{payment.data.payableAmount} {payment.data.currency}</strong></div>}
        {payment.data && payment.data.refundedAmount > 0 && <div className="summary-line"><span>Возвращено</span><strong>{payment.data.refundedAmount} {payment.data.currency}</strong></div>}
        {payment.data && <div className="summary-line"><span>Провайдер</span><strong>{payment.data.provider} · {payment.data.type}</strong></div>}
        {!paymentId && <FormError message="Платеж не передан в return URL. Вернитесь в приложение и создайте оплату повторно." />}
        {payment.isError && <FormError error={payment.error} message="Не удалось проверить статус платежа. Нажмите, чтобы повторить." onRetry={() => payment.refetch()} />}
        {isPending && <LoadingState label="Страница обновляет статус каждые 3 секунды, пока платеж не станет финальным." />}
        {isSuccess && <p className="copy">Платеж подтвержден. Бронь будет переведена в CONFIRMED после обработки webhook.</p>}
        {isFailed && <p className="copy">Платеж не прошел. Можно вернуться к бронированию и создать новый платеж.</p>}
        {paymentId && <button className="primary secondary" disabled={payment.isFetching} onClick={() => payment.refetch()}>{payment.isFetching ? "Проверяем" : "Проверить снова"}</button>}
        {isSuccess && <Link className="primary link-button" href="/account">Открыть историю</Link>}
        <Link className="primary link-button" href="/">Вернуться в приложение</Link>
      </section>
    </main>
  );
}

function normalizeStatus(status: string) {
  return status.trim().toUpperCase() || "PROCESSING";
}

function statusMessage(status: string, fetching: boolean) {
  if (fetching) return "Проверяем актуальный статус платежа...";
  if (status === "SUCCESS") return "Платеж успешно подтвержден провайдером.";
  if (status === "FAILED") return "Платеж отклонен или отменен.";
  if (status === "REFUNDED" || status === "PARTIALLY_REFUNDED") return "По платежу выполнен возврат.";
  return "Платеж создан и ожидает webhook от платежного провайдера.";
}
