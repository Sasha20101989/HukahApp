"use client";

import { CalendarDays, Clock3, CreditCard, Flame, Users } from "lucide-react";
import { useMemo, useState } from "react";
import { createBooking, createDepositPayment, loginClient, registerClient } from "../lib/api";

const mixes = [
  { id: "70000000-0000-0000-0000-000000000001", name: "Berry Ice", strength: "MEDIUM", taste: "BERRY_FRESH", price: 850 },
  { id: "70000000-0000-0000-0000-000000000002", name: "Sweet Fresh", strength: "LIGHT", taste: "SWEET_FRESH", price: 820 },
  { id: "70000000-0000-0000-0000-000000000003", name: "Dark Citrus", strength: "STRONG", taste: "CITRUS", price: 920 }
];

const tables = [
  { id: "30000000-0000-0000-0000-000000000001", name: "Стол 1", capacity: 4 },
  { id: "30000000-0000-0000-0000-000000000002", name: "Стол 2", capacity: 6 }
];

export default function ClientBookingPage() {
  const [date, setDate] = useState("2026-05-01");
  const [time, setTime] = useState("20:00");
  const [guests, setGuests] = useState(4);
  const [tableId, setTableId] = useState(tables[0].id);
  const [mixId, setMixId] = useState(mixes[0].id);
  const [clientName, setClientName] = useState("");
  const [clientPhone, setClientPhone] = useState("");
  const [clientEmail, setClientEmail] = useState("");
  const [promocode, setPromocode] = useState("");
  const [comment, setComment] = useState("");
  const [submitState, setSubmitState] = useState<"idle" | "loading" | "success" | "error">("idle");
  const [submitMessage, setSubmitMessage] = useState("");

  const selectedTable = tables.find((table) => table.id === tableId) ?? tables[0];
  const selectedMix = mixes.find((mix) => mix.id === mixId) ?? mixes[0];
  const deposit = useMemo(() => (guests >= 5 ? 3000 : 2000), [guests]);

  async function handleReserve() {
    setSubmitState("loading");
    setSubmitMessage("");

    try {
      if (!clientName.trim() || !clientPhone.trim()) {
        throw new Error("Укажите имя и телефон.");
      }

      let clientId: string;
      let accessToken: string;
      try {
        const auth = await registerClient({
          name: clientName.trim(),
          phone: clientPhone.trim(),
          email: clientEmail.trim() || undefined,
          password: clientPhone.trim()
        });
        clientId = auth.userId;
        accessToken = auth.accessToken;
      } catch {
        const auth = await loginClient(clientPhone.trim(), clientPhone.trim());
        clientId = auth.userId;
        accessToken = auth.accessToken;
      }

      const start = new Date(`${date}T${time}:00.000Z`);
      const end = new Date(start.getTime() + 2 * 60 * 60 * 1000);
      const booking = await createBooking({
        branchId: "10000000-0000-0000-0000-000000000001",
        tableId,
        clientId,
        startTime: start.toISOString(),
        endTime: end.toISOString(),
        guestsCount: guests,
        hookahId: "40000000-0000-0000-0000-000000000001",
        bowlId: "50000000-0000-0000-0000-000000000001",
        mixId,
        comment,
        depositAmount: deposit
      }, accessToken);

      const payment = await createDepositPayment({
        clientId,
        bookingId: booking.id,
        amount: deposit,
        currency: "RUB",
        type: "DEPOSIT",
        provider: "YOOKASSA",
        promocode: promocode.trim() || undefined
      }, accessToken);

      setSubmitState("success");
      setSubmitMessage(`Бронь создана. Ссылка на оплату: ${payment.paymentUrl}`);
    } catch (error) {
      setSubmitState("error");
      setSubmitMessage(error instanceof Error ? error.message : "Не удалось создать бронь.");
    }
  }

  return (
    <main className="booking-shell">
      <header className="app-header">
        <div className="logo">
          <span className="logo-mark"><Flame size={20} /></span>
          Hookah Place
        </div>
        <button className="secondary">Мои брони</button>
      </header>

      <div className="layout">
        <section className="panel">
          <h1>Бронирование стола</h1>
          <p className="copy">Выберите время, количество гостей, стол и микс. Если депозит обязателен, бронь подтвердится после оплаты.</p>

          <div className="form-grid">
            <div className="two">
              <label className="field">
                <span><CalendarDays size={14} /> Дата</span>
                <input value={date} onChange={(event) => setDate(event.target.value)} type="date" />
              </label>
              <label className="field">
                <span><Clock3 size={14} /> Время</span>
                <input value={time} onChange={(event) => setTime(event.target.value)} type="time" />
              </label>
            </div>

            <label className="field">
              <span><Users size={14} /> Гости</span>
              <select value={guests} onChange={(event) => setGuests(Number(event.target.value))}>
                {[2, 3, 4, 5, 6, 7, 8].map((value) => (
                  <option value={value} key={value}>{value}</option>
                ))}
              </select>
            </label>

            <div className="two">
              <label className="field">
                <span>Имя</span>
                <input value={clientName} onChange={(event) => setClientName(event.target.value)} placeholder="Александр" />
              </label>
              <label className="field">
                <span>Телефон</span>
                <input value={clientPhone} onChange={(event) => setClientPhone(event.target.value)} placeholder="+79990000000" />
              </label>
            </div>

            <label className="field">
              <span>Email</span>
              <input value={clientEmail} onChange={(event) => setClientEmail(event.target.value)} placeholder="client@mail.com" />
            </label>

            <label className="field">
              <span>Промокод</span>
              <input value={promocode} onChange={(event) => setPromocode(event.target.value)} placeholder="HOOKAH20" />
            </label>

            <label className="field">
              <span>Комментарий</span>
              <textarea value={comment} onChange={(event) => setComment(event.target.value)} placeholder="Например: день рождения, нужна средняя крепость без сильного холода" />
            </label>
          </div>
        </section>

        <section className="panel">
          <h2>Доступные столы</h2>
          <div className="table-list">
            {tables.map((table) => (
              <button className={`choice ${table.id === tableId ? "active" : ""}`} onClick={() => setTableId(table.id)} key={table.id}>
                <span>
                  <strong>{table.name}</strong>
                  <span className="copy"> · до {table.capacity} гостей</span>
                </span>
                <span className="badge">FREE</span>
              </button>
            ))}
          </div>

          <h2 style={{ marginTop: 18 }}>Микс</h2>
          <div className="mix-list">
            {mixes.map((mix) => (
              <button className={`choice ${mix.id === mixId ? "active" : ""}`} onClick={() => setMixId(mix.id)} key={mix.id}>
                <span>
                  <strong>{mix.name}</strong>
                  <span className="copy"> · {mix.taste}</span>
                </span>
                <span className="badge">{mix.strength}</span>
              </button>
            ))}
          </div>

          <div className="summary">
            <div className="summary-line">
              <span>Дата и время</span>
              <strong>{date} · {time}</strong>
            </div>
            <div className="summary-line">
              <span>Стол</span>
              <strong>{selectedTable.name}</strong>
            </div>
            <div className="summary-line">
              <span>Микс</span>
              <strong>{selectedMix.name}</strong>
            </div>
            <div className="summary-line">
              <span>Депозит</span>
              <strong className="amount">{deposit} ₽</strong>
            </div>
            <button className="primary" onClick={handleReserve} disabled={submitState === "loading"}>
              <CreditCard size={18} />{submitState === "loading" ? "Создаем бронь" : "Забронировать и оплатить"}
            </button>
            {submitMessage && <p className={`notice ${submitState}`}>{submitMessage}</p>}
          </div>
        </section>
      </div>
    </main>
  );
}
