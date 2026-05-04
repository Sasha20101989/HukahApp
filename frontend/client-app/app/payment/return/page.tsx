import { Suspense } from "react";
import { PaymentReturnStatus } from "./status";

export default function PaymentReturnPage() {
  return (
    <Suspense fallback={<main className="booking-shell compact"><section className="panel success-step"><h1>Статус оплаты: processing</h1><p>Проверяем платеж...</p></section></main>}>
      <PaymentReturnStatus />
    </Suspense>
  );
}
