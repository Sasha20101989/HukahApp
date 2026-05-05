import { Suspense } from "react";
import { PaymentReturnStatus } from "./status";

export default async function PaymentReturnPage({ searchParams }: { searchParams?: Promise<Record<string, string | string[] | undefined>> }) {
  const resolved = searchParams ? await searchParams : undefined;
  const paymentId = getFirst(resolved?.paymentId);
  const status = getFirst(resolved?.status);
  const normalized = (status ?? "PROCESSING").trim().toUpperCase() || "PROCESSING";

  return (
    <main className="booking-shell compact">
      <section className="panel success-step">
        <h1>Статус оплаты: {paymentId ? normalized : "PROCESSING"}</h1>
        {!paymentId && <p>Платеж не передан</p>}
        {paymentId && normalized === "PROCESSING" && <p>Проверяем платеж...</p>}
        <Suspense fallback={null}>
          <PaymentReturnStatus />
        </Suspense>
      </section>
    </main>
  );
}

function getFirst(value: string | string[] | undefined) {
  if (!value) return undefined;
  return Array.isArray(value) ? value[0] : value;
}
