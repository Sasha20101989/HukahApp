"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarDays, CheckCircle2, Clock3, CreditCard, Flame, History, MessageSquare, Star, Users, WalletCards } from "lucide-react";
import { useMemo, useState } from "react";
import { buildBookingWindow, createBooking, createDepositPayment, createHold, createReview, demoBranches, demoMixes, demoReviews, demoTables, getJson, loginClient, registerClient, timeLabel, type Booking, type Mix, type Table } from "../lib/api";
import { useClientStore } from "../lib/store";

export default function ClientBookingPage() {
  const queryClient = useQueryClient();
  const { step, session, draft, setStep, setSession, setDraft } = useClientStore();
  const [notice, setNotice] = useState("");
  const [reviewText, setReviewText] = useState("");
  const [rating, setRating] = useState(5);

  const branches = useQuery({ queryKey: ["branches"], queryFn: () => getJson("/api/branches", demoBranches) });
  const mixes = useQuery({ queryKey: ["mixes"], queryFn: () => getJson<Mix[]>("/api/mixes?publicOnly=true", demoMixes) });
  const reviews = useQuery({ queryKey: ["reviews"], queryFn: () => getJson("/api/reviews", demoReviews) });
  const { startTime, endTime } = buildBookingWindow(draft.date, draft.time);
  const availability = useQuery({
    queryKey: ["availability", draft.branchId, draft.date, draft.time, draft.guests],
    queryFn: () => getJson<Table[]>(`/api/bookings/availability?branchId=${draft.branchId}&date=${draft.date}&time=${draft.time}&guestsCount=${draft.guests}`, demoTables)
  });
  const history = useQuery({
    queryKey: ["booking-history", session.userId, draft.branchId],
    enabled: Boolean(session.userId),
    queryFn: () => getJson<Booking[]>(`/api/bookings?branchId=${draft.branchId}`, [], session.accessToken)
  });

  const selectedBranch = branches.data?.find((branch) => branch.id === draft.branchId) ?? demoBranches[0];
  const selectedMix = mixes.data?.find((mix) => mix.id === draft.mixId) ?? demoMixes[0];
  const selectedTable = availability.data?.find((table) => table.id === draft.tableId) ?? availability.data?.[0] ?? demoTables[0];
  const deposit = useMemo(() => (draft.guests >= 5 ? 3000 : 2000), [draft.guests]);

  const authMutation = useMutation({
    mutationFn: async () => {
      if (!session.name.trim() || !session.phone.trim()) throw new Error("Укажите имя и телефон.");
      try {
        return await registerClient({ name: session.name.trim(), phone: session.phone.trim(), email: session.email.trim() || undefined, password: session.phone.trim() });
      } catch {
        return loginClient(session.phone.trim(), session.phone.trim());
      }
    },
    onSuccess: (auth) => {
      setSession(auth);
      setNotice("Профиль готов. Теперь выберите время и стол.");
      setStep("time");
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось войти")
  });

  const holdMutation = useMutation({
    mutationFn: async () => {
      if (!session.userId) throw new Error("Сначала войдите или зарегистрируйтесь.");
      const tableId = selectedTable.id;
      const hold = await createHold({ branchId: draft.branchId, tableId, clientId: session.userId, startTime, endTime, guestsCount: draft.guests }, session.accessToken);
      return hold;
    },
    onSuccess: (hold) => {
      setDraft({ tableId: hold.tableId, holdId: hold.id });
      setNotice(`Стол удерживается до ${timeLabel(hold.expiresAt)}.`);
      setStep("mix");
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось удержать стол")
  });

  const bookingMutation = useMutation({
    mutationFn: async () => {
      if (!session.userId) throw new Error("Сессия клиента не найдена.");
      const booking = await createBooking({
        branchId: draft.branchId,
        tableId: draft.tableId ?? selectedTable.id,
        clientId: session.userId,
        startTime,
        endTime,
        guestsCount: draft.guests,
        hookahId: draft.hookahId,
        bowlId: selectedMix.bowlId,
        mixId: selectedMix.id,
        comment: draft.comment,
        depositAmount: deposit,
        holdId: draft.holdId
      }, session.accessToken);
      const payment = await createDepositPayment({ clientId: session.userId, bookingId: booking.id, amount: deposit, currency: "RUB", type: "DEPOSIT", provider: "YOOKASSA", promocode: draft.promocode.trim() || undefined }, session.accessToken);
      return { booking, payment };
    },
    onSuccess: async ({ payment }) => {
      setNotice(`Бронь создана. Оплата: ${payment.paymentUrl}`);
      setStep("payment");
      await queryClient.invalidateQueries({ queryKey: ["booking-history"] });
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось создать бронь")
  });

  const reviewMutation = useMutation({
    mutationFn: () => {
      if (!session.userId) throw new Error("Сначала войдите.");
      return createReview({ clientId: session.userId, mixId: selectedMix.id, rating, text: reviewText }, session.accessToken);
    },
    onSuccess: async () => {
      setReviewText("");
      setNotice("Отзыв отправлен.");
      await queryClient.invalidateQueries({ queryKey: ["reviews"] });
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось отправить отзыв")
  });

  return (
    <main className="booking-shell">
      <header className="app-header">
        <div className="logo"><span className="logo-mark"><Flame size={20} /></span><span>Hookah Place</span></div>
        <nav className="tabs" aria-label="Client flow">
          <button className={step === "profile" ? "active" : ""} onClick={() => setStep("profile")}>Профиль</button>
          <button className={step === "time" ? "active" : ""} onClick={() => setStep("time")}>Время</button>
          <button className={step === "mix" ? "active" : ""} onClick={() => setStep("mix")}>Микс</button>
          <button className={step === "history" ? "active" : ""} onClick={() => setStep("history")}>История</button>
        </nav>
      </header>

      <section className="hero">
        <div><p className="eyebrow">PWA booking</p><h1>Бронь, депозит, микс и отзывы в одном клиентском приложении.</h1></div>
        <div className="hero-card"><WalletCards size={24} /><span>Депозит</span><strong>{deposit} ₽</strong></div>
      </section>

      <div className="layout">
        <section className="panel main-panel">
          {step === "profile" && <ProfileStep session={session} setSession={setSession} onSubmit={() => authMutation.mutate()} loading={authMutation.isPending} />}
          {step === "time" && <TimeStep branches={branches.data ?? []} tables={availability.data ?? []} selectedTable={selectedTable} draft={draft} setDraft={setDraft} onHold={() => holdMutation.mutate()} loading={holdMutation.isPending} />}
          {step === "mix" && <MixStep mixes={mixes.data ?? []} selectedMix={selectedMix} draft={draft} setDraft={setDraft} onSubmit={() => bookingMutation.mutate()} loading={bookingMutation.isPending} />}
          {step === "payment" && <PaymentStep />}
          {step === "history" && <HistoryStep bookings={history.data ?? []} reviews={reviews.data ?? []} rating={rating} setRating={setRating} reviewText={reviewText} setReviewText={setReviewText} onReview={() => reviewMutation.mutate()} />}
        </section>

        <aside className="panel summary-panel">
          <h2>Итог брони</h2>
          <SummaryLine label="Филиал" value={selectedBranch.name} />
          <SummaryLine label="Дата" value={`${draft.date} · ${draft.time}`} />
          <SummaryLine label="Гости" value={String(draft.guests)} />
          <SummaryLine label="Стол" value={selectedTable.name} />
          <SummaryLine label="Микс" value={selectedMix.name} />
          <SummaryLine label="Статус hold" value={draft.holdId ? "активен" : "не создан"} />
          <div className="deposit"><span>К оплате</span><strong>{deposit} ₽</strong></div>
          {notice && <button className="notice" onClick={() => setNotice("")}>{notice}</button>}
        </aside>
      </div>
    </main>
  );
}

function ProfileStep({ session, setSession, onSubmit, loading }: { session: { name: string; phone: string; email: string }; setSession: (value: Partial<typeof session>) => void; onSubmit: () => void; loading: boolean }) {
  return <div className="step"><h2>Профиль клиента</h2><p className="copy">Телефон используется как пароль в demo-flow. В production здесь будет SMS/Telegram OTP.</p><div className="form-grid"><Field label="Имя" value={session.name} onChange={(name) => setSession({ name })} placeholder="Александр" /><Field label="Телефон" value={session.phone} onChange={(phone) => setSession({ phone })} placeholder="+79990000000" /><Field label="Email" value={session.email} onChange={(email) => setSession({ email })} placeholder="client@mail.com" /></div><button className="primary" onClick={onSubmit} disabled={loading}><Users size={18} />{loading ? "Входим" : "Продолжить"}</button></div>;
}

function TimeStep({ branches, tables, selectedTable, draft, setDraft, onHold, loading }: { branches: { id: string; name: string }[]; tables: Table[]; selectedTable: Table; draft: { branchId: string; date: string; time: string; guests: number; tableId?: string }; setDraft: (value: Partial<typeof draft>) => void; onHold: () => void; loading: boolean }) {
  return <div className="step"><h2>Время и стол</h2><div className="form-grid two"><label className="field"><span>Филиал</span><select value={draft.branchId} onChange={(event) => setDraft({ branchId: event.target.value })}>{branches.map((branch) => <option value={branch.id} key={branch.id}>{branch.name}</option>)}</select></label><Field label="Дата" type="date" value={draft.date} onChange={(date) => setDraft({ date })} /><Field label="Время" type="time" value={draft.time} onChange={(time) => setDraft({ time })} /><label className="field"><span>Гости</span><select value={draft.guests} onChange={(event) => setDraft({ guests: Number(event.target.value) })}>{[2,3,4,5,6,7,8].map((value) => <option value={value} key={value}>{value}</option>)}</select></label></div><div className="choice-grid">{tables.map((table) => <button className={`choice ${selectedTable.id === table.id ? "active" : ""}`} onClick={() => setDraft({ tableId: table.id })} key={table.id}><strong>{table.name}</strong><span>до {table.capacity} гостей</span></button>)}</div><button className="primary" onClick={onHold} disabled={loading}><Clock3 size={18} />{loading ? "Удерживаем" : "Удержать стол на 10 минут"}</button></div>;
}

function MixStep({ mixes, selectedMix, draft, setDraft, onSubmit, loading }: { mixes: Mix[]; selectedMix: Mix; draft: { mixId: string; comment: string; promocode: string }; setDraft: (value: Partial<typeof draft>) => void; onSubmit: () => void; loading: boolean }) {
  return <div className="step"><h2>Микс и оплата</h2><div className="choice-grid">{mixes.map((mix) => <button className={`choice mix ${selectedMix.id === mix.id ? "active" : ""}`} onClick={() => setDraft({ mixId: mix.id })} key={mix.id}><strong>{mix.name}</strong><span>{mix.tasteProfile} · {mix.strength} · {mix.price} ₽</span></button>)}</div><label className="field"><span>Комментарий</span><textarea value={draft.comment} onChange={(event) => setDraft({ comment: event.target.value })} placeholder="Средняя крепость, без сильного холода" /></label><Field label="Промокод" value={draft.promocode} onChange={(promocode) => setDraft({ promocode })} placeholder="HOOKAH20" /><button className="primary" onClick={onSubmit} disabled={loading}><CreditCard size={18} />{loading ? "Создаем" : "Забронировать и оплатить"}</button></div>;
}

function PaymentStep() {
  return <div className="success-step"><CheckCircle2 size={46} /><h2>Бронь создана</h2><p>После успешного webhook платежа бронь станет CONFIRMED, клиент и менеджер получат уведомления.</p></div>;
}

function HistoryStep({ bookings, reviews, rating, setRating, reviewText, setReviewText, onReview }: { bookings: Booking[]; reviews: { id: string; rating: number; text?: string | null }[]; rating: number; setRating: (value: number) => void; reviewText: string; setReviewText: (value: string) => void; onReview: () => void }) {
  return <div className="step"><h2>История и отзывы</h2><div className="history-list">{bookings.length === 0 ? <p className="copy">История появится после первой брони.</p> : bookings.map((booking) => <article key={booking.id}><History size={16} /><span>{timeLabel(booking.startTime)} · {booking.status}</span></article>)}</div><h3>Оставить отзыв</h3><div className="rating">{[1,2,3,4,5].map((value) => <button className={value <= rating ? "active" : ""} onClick={() => setRating(value)} key={value}><Star size={18} /></button>)}</div><label className="field"><span>Текст отзыва</span><textarea value={reviewText} onChange={(event) => setReviewText(event.target.value)} placeholder="Что понравилось в миксе и сервисе?" /></label><button className="primary" onClick={onReview}><MessageSquare size={18} />Отправить отзыв</button><div className="review-feed">{reviews.map((review) => <blockquote key={review.id}>★ {review.rating} · {review.text}</blockquote>)}</div></div>;
}

function Field({ label, value, onChange, placeholder, type = "text" }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string; type?: string }) {
  return <label className="field"><span>{label}</span><input type={type} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} /></label>;
}

function SummaryLine({ label, value }: { label: string; value: string }) {
  return <div className="summary-line"><span>{label}</span><strong>{value}</strong></div>;
}
