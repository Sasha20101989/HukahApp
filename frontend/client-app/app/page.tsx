"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, CalendarDays, CheckCircle2, Clock3, CreditCard, Flame, History, LogOut, MessageSquare, Star, Users, WalletCards } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { buildBookingWindow, configureApiAuth, createBooking, createDepositPayment, createHold, createReview, deleteReview, getJson, getNotificationPreference, loginClient, logoutAuth, registerClient, timeLabel, updateClientProfile, updateNotificationPreference, updateReview, type Booking, type Branch, type Mix, type NotificationPreference, type Review, type Table } from "../lib/api";
import { hydrateClientSession, useClientStore } from "../lib/store";
import { EmptyState, FieldError, FormError, LoadingState, MutationToast } from "../lib/ui";
import { isValidEmail, isValidPhone, normalizePhone, normalizePhoneInput } from "../lib/validation";

export default function ClientBookingPage() {
  const queryClient = useQueryClient();
  const { step, session, draft, hydrated, setStep, setSession, setDraft, logout } = useClientStore();
  const [notice, setNotice] = useState("");
  const [reviewText, setReviewText] = useState("");
  const [rating, setRating] = useState(5);
  const [paymentUrl, setPaymentUrl] = useState("");
  const [paymentMode, setPaymentMode] = useState<"PROVIDER_REDIRECT" | "LOCAL_RETURN">("LOCAL_RETURN");

  useEffect(() => hydrateClientSession(), []);

  useEffect(() => configureApiAuth({
    getRefreshToken: () => useClientStore.getState().session.refreshToken,
    onRefresh: setSession,
    onUnauthorized: logout
  }), [logout, setSession]);

  const authorized = useCallback(async <T,>(call: (accessToken: string) => Promise<T>): Promise<T> => {
    if (!session.accessToken) throw new Error("Сессия клиента не найдена.");
    return call(session.accessToken);
  }, [session.accessToken]);

  const handleLogout = useCallback(async () => {
    if (session.refreshToken) {
      try { await logoutAuth(session.refreshToken); } catch { /* local logout still wins */ }
    }
    logout();
  }, [logout, session.refreshToken]);

  const branches = useQuery({ queryKey: ["branches"], queryFn: () => getJson<Branch[]>("/api/branches") });
  const mixes = useQuery({ queryKey: ["mixes"], queryFn: () => getJson<Mix[]>("/api/mixes?publicOnly=true") });
  const reviews = useQuery({ queryKey: ["reviews"], queryFn: () => getJson<Review[]>("/api/reviews") });
  const { startTime, endTime } = buildBookingWindow(draft.date, draft.time);
  const availability = useQuery({
    queryKey: ["availability", draft.branchId, draft.date, draft.time, draft.guests],
    enabled: Boolean(draft.branchId),
    queryFn: () => getJson<Table[]>(`/api/bookings/availability?branchId=${draft.branchId}&date=${draft.date}&time=${draft.time}&guestsCount=${draft.guests}`)
  });
  const history = useQuery({
    queryKey: ["booking-history", session.userId, draft.branchId],
    enabled: Boolean(session.userId && draft.branchId),
    queryFn: () => authorized((accessToken) => getJson<Booking[]>(`/api/bookings?branchId=${draft.branchId}`, accessToken))
  });
  const preferences = useQuery({
    queryKey: ["notification-preferences", session.userId],
    enabled: Boolean(session.userId && session.accessToken),
    queryFn: () => authorized((accessToken) => getNotificationPreference(session.userId!, accessToken))
  });

  const selectedBranch = branches.data?.find((branch) => branch.id === draft.branchId);
  const selectedMix = mixes.data?.find((mix) => mix.id === draft.mixId);
  const selectedTable = availability.data?.find((table) => table.id === draft.tableId);
  const backendError = branches.error ?? mixes.error ?? reviews.error ?? availability.error ?? history.error ?? preferences.error;
  const deposit = useMemo(() => (draft.guests >= 5 ? 3000 : 2000), [draft.guests]);

  const authMutation = useMutation({
    mutationFn: async () => {
      if (!session.name.trim() || !isValidPhone(session.phone) || session.password.length < 6 || !isValidEmail(session.email)) throw new Error("Проверьте имя, телефон, email и пароль от 6 символов.");
      try {
        return await registerClient({ name: session.name.trim(), phone: normalizePhone(session.phone), email: session.email.trim() || undefined, password: session.password });
      } catch {
        return loginClient(normalizePhone(session.phone), session.password);
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
      if (!draft.branchId) throw new Error("Выберите филиал.");
      if (!selectedTable) throw new Error("Нет доступного стола для выбранного времени.");
      const tableId = selectedTable.id;
      const hold = await authorized((accessToken) => createHold({ branchId: draft.branchId, tableId, clientId: session.userId!, startTime, endTime, guestsCount: draft.guests }, accessToken));
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
      if (!draft.branchId) throw new Error("Выберите филиал.");
      if (!selectedTable || !selectedMix) throw new Error("Выберите доступный стол и микс.");
      const booking = await authorized((accessToken) => createBooking({
        branchId: draft.branchId,
        tableId: selectedTable.id,
        clientId: session.userId!,
        startTime,
        endTime,
        guestsCount: draft.guests,
        hookahId: draft.hookahId || undefined,
        bowlId: selectedMix.bowlId,
        mixId: selectedMix.id,
        comment: draft.comment,
        depositAmount: deposit,
        holdId: draft.holdId
      }, accessToken));
      const returnUrl = `${window.location.origin}/payment/return`;
      const payment = await authorized((accessToken) => createDepositPayment({ clientId: session.userId!, bookingId: booking.id, amount: deposit, currency: "RUB", type: "DEPOSIT", provider: "YOOKASSA", promocode: draft.promocode.trim() || undefined, returnUrl }, accessToken));
      setDraft({ bookingId: booking.id, paymentId: payment.paymentId, paymentUrl: payment.paymentUrl, paymentReturnUrl: payment.returnUrl });
      return { booking, payment };
    },
    onSuccess: async ({ payment }) => {
      setDraft({ paymentId: payment.paymentId, paymentUrl: payment.paymentUrl, paymentReturnUrl: payment.returnUrl });
      setPaymentUrl(payment.paymentUrl);
      setPaymentMode(payment.checkoutMode);
      setNotice(payment.checkoutMode === "PROVIDER_REDIRECT" ? "Бронь создана. Перенаправляем к платежному провайдеру." : "Бронь создана. Открываем локальную страницу ожидания webhook.");
      setStep("payment");
      await queryClient.invalidateQueries({ queryKey: ["booking-history"] });
      window.location.assign(payment.paymentUrl);
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось создать бронь")
  });

  const reviewMutation = useMutation({
    mutationFn: () => {
      if (!session.userId) throw new Error("Сначала войдите.");
      if (!selectedMix) throw new Error("Выберите микс.");
      return authorized((accessToken) => createReview({ clientId: session.userId!, mixId: selectedMix.id, rating, text: reviewText }, accessToken));
    },
    onSuccess: async () => {
      setReviewText("");
      setNotice("Отзыв отправлен.");
      await queryClient.invalidateQueries({ queryKey: ["reviews"] });
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось отправить отзыв")
  });

  const preferencesMutation = useMutation({
    mutationFn: (payload: Omit<NotificationPreference, "userId">) => {
      if (!session.userId) throw new Error("Сначала войдите.");
      return authorized((accessToken) => updateNotificationPreference(session.userId!, payload, accessToken));
    },
    onSuccess: async () => {
      setNotice("Настройки уведомлений сохранены.");
      await queryClient.invalidateQueries({ queryKey: ["notification-preferences"] });
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось сохранить настройки")
  });
  const profileMutation = useMutation({
    mutationFn: () => {
      if (!session.userId) throw new Error("Сначала войдите.");
      if (!isValidEmail(session.email)) throw new Error("Email должен быть корректным.");
      return authorized((accessToken) => updateClientProfile(session.userId!, { name: session.name.trim(), email: session.email.trim() || undefined }, accessToken));
    },
    onSuccess: (profile) => {
      setSession({ name: profile.name, email: profile.email ?? "" });
      setNotice("Профиль обновлен.");
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось обновить профиль")
  });
  const reviewUpdateMutation = useMutation({
    mutationFn: ({ id, rating, text }: { id: string; rating: number; text: string }) => authorized((accessToken) => updateReview(id, { rating, text }, accessToken)),
    onSuccess: async () => { setNotice("Отзыв обновлен."); await queryClient.invalidateQueries({ queryKey: ["reviews"] }); },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось обновить отзыв")
  });
  const reviewDeleteMutation = useMutation({
    mutationFn: (id: string) => authorized((accessToken) => deleteReview(id, accessToken)),
    onSuccess: async () => { setNotice("Отзыв удален."); await queryClient.invalidateQueries({ queryKey: ["reviews"] }); },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось удалить отзыв")
  });

  if (!hydrated) return <main className="booking-shell"><section className="panel"><LoadingState label="Загружаем сессию..." /></section></main>;

  return (
    <main className="booking-shell">
      <header className="app-header">
        <div className="logo"><span className="logo-mark"><Flame size={20} /></span><span>Hookah Place</span></div>
        <nav className="tabs" aria-label="Client flow">
          <button className={step === "profile" ? "active" : ""} onClick={() => setStep("profile")}>Профиль</button>
          <button className={step === "time" ? "active" : ""} onClick={() => session.userId ? setStep("time") : setNotice("Сначала войдите или зарегистрируйтесь.")}>Время</button>
          <button className={step === "mix" ? "active" : ""} onClick={() => session.userId ? setStep("mix") : setNotice("Сначала войдите или зарегистрируйтесь.")}>Микс</button>
          <button className={step === "history" ? "active" : ""} onClick={() => session.userId ? setStep("history") : setNotice("Сначала войдите или зарегистрируйтесь.")}>История</button>
        </nav>
        <div className="account-actions">{session.accessToken && <a className="logout link-button" href="/account">Аккаунт</a>}{session.accessToken && <button className="logout" onClick={handleLogout}><LogOut size={15} />Выйти</button>}</div>
      </header>

      <section className="hero">
        <div><p className="eyebrow">PWA booking</p><h1>Бронь, депозит, микс и отзывы в одном клиентском приложении.</h1></div>
        <div className="hero-card"><WalletCards size={24} /><span>Депозит</span><strong>{deposit} ₽</strong></div>
      </section>

      <div className="layout">
        <section className="panel main-panel">
          {backendError && <FormError error={backendError} onRetry={() => queryClient.invalidateQueries()} />}
          {step === "profile" && <ProfileStep session={session} setSession={setSession} onSubmit={() => authMutation.mutate()} onSaveProfile={() => profileMutation.mutate()} loading={authMutation.isPending || profileMutation.isPending} preferences={preferences.data} onPreferences={(value) => preferencesMutation.mutate(value)} />}
          {step === "time" && <TimeStep branches={branches.data ?? []} tables={availability.data ?? []} selectedTable={selectedTable} draft={draft} setDraft={setDraft} onHold={() => holdMutation.mutate()} loading={holdMutation.isPending} />}
          {step === "mix" && <MixStep mixes={mixes.data ?? []} selectedMix={selectedMix} draft={draft} setDraft={setDraft} onSubmit={() => bookingMutation.mutate()} loading={bookingMutation.isPending} />}
          {step === "payment" && <PaymentStep paymentUrl={paymentUrl} mode={paymentMode} />}
          {step === "history" && <HistoryStep clientId={session.userId} bookings={history.data ?? []} reviews={reviews.data ?? []} rating={rating} setRating={setRating} reviewText={reviewText} setReviewText={setReviewText} onReview={() => reviewMutation.mutate()} onUpdateReview={(id, nextRating, nextText) => reviewUpdateMutation.mutate({ id, rating: nextRating, text: nextText })} onDeleteReview={(id) => reviewDeleteMutation.mutate(id)} />}
          <MutationToast mutation={authMutation} successMessage="Профиль готов" />
          <MutationToast mutation={holdMutation} successMessage="Стол удержан" />
          <MutationToast mutation={bookingMutation} successMessage="Бронь создана" />
          <MutationToast mutation={profileMutation} successMessage="Профиль обновлен" />
          <MutationToast mutation={preferencesMutation} successMessage="Настройки сохранены" />
          <MutationToast mutation={reviewMutation} successMessage="Отзыв отправлен" />
          <MutationToast mutation={reviewUpdateMutation} successMessage="Отзыв обновлен" />
          <MutationToast mutation={reviewDeleteMutation} successMessage="Отзыв удален" />
        </section>

        <aside className="panel summary-panel">
          <h2>Итог брони</h2>
          <SummaryLine label="Филиал" value={selectedBranch?.name ?? "не выбран"} />
          <SummaryLine label="Дата" value={`${draft.date} · ${draft.time}`} />
          <SummaryLine label="Гости" value={String(draft.guests)} />
          <SummaryLine label="Стол" value={selectedTable?.name ?? "нет доступных"} />
          <SummaryLine label="Микс" value={selectedMix?.name ?? "нет публичных миксов"} />
          <SummaryLine label="Статус hold" value={draft.holdId ? "активен" : "не создан"} />
          <div className="deposit"><span>К оплате</span><strong>{deposit} ₽</strong></div>
          {notice && <button className="notice" onClick={() => setNotice("")}>{notice}</button>}
        </aside>
      </div>
    </main>
  );
}

function ProfileStep({ session, setSession, onSubmit, onSaveProfile, loading, preferences, onPreferences }: { session: { userId?: string; name: string; phone: string; email: string; password: string }; setSession: (value: Partial<typeof session>) => void; onSubmit: () => void; onSaveProfile: () => void; loading: boolean; preferences?: NotificationPreference; onPreferences: (value: Omit<NotificationPreference, "userId">) => void }) {
  const passwordError = session.password && session.password.length < 6 ? "Пароль должен быть не короче 6 символов." : "";
  const phoneError = session.phone && !isValidPhone(session.phone) ? "Телефон должен быть в формате +79990000000." : "";
  const emailError = !isValidEmail(session.email) ? "Email должен быть корректным." : "";
  return <div className="step"><h2>Профиль клиента</h2><p className="copy">Войдите или зарегистрируйтесь по телефону и паролю. Имя и email сохраняются через backend profile API.</p><div className="form-grid"><Field label="Имя" value={session.name} onChange={(name) => setSession({ name })} placeholder="Александр" /><Field label="Телефон" type="tel" value={session.phone} onChange={(phone) => setSession({ phone: normalizePhoneInput(phone) })} placeholder="+79990000000" /><Field label="Email" type="email" value={session.email} onChange={(email) => setSession({ email })} placeholder="client@mail.com" /><Field label="Пароль" type="password" value={session.password} onChange={(password) => setSession({ password })} placeholder="Минимум 6 символов" /></div><FieldError message={phoneError || emailError || passwordError} /><div className="action-row"><button className="primary" onClick={onSubmit} disabled={loading || !session.name.trim() || !isValidPhone(session.phone) || !isValidEmail(session.email) || session.password.length < 6}><Users size={18} />{loading ? "Входим" : "Продолжить"}</button>{session.userId && <button className="primary secondary" onClick={onSaveProfile} disabled={loading || !isValidEmail(session.email)}>Сохранить профиль</button>}</div>{session.userId && <PreferencePanel preferences={preferences} onSave={onPreferences} />}</div>;
}

function PreferencePanel({ preferences, onSave }: { preferences?: NotificationPreference; onSave: (value: Omit<NotificationPreference, "userId">) => void }) {
  const value = preferences
    ? { crmEnabled: preferences.crmEnabled, telegramEnabled: preferences.telegramEnabled, smsEnabled: preferences.smsEnabled, emailEnabled: preferences.emailEnabled, pushEnabled: preferences.pushEnabled }
    : { crmEnabled: true, telegramEnabled: true, smsEnabled: true, emailEnabled: true, pushEnabled: true };
  return <div className="preference-card"><h3><Bell size={16} />Уведомления</h3>{(["crmEnabled", "telegramEnabled", "smsEnabled", "emailEnabled", "pushEnabled"] as const).map((key) => <label key={key}><input type="checkbox" checked={value[key]} onChange={(event) => onSave({ ...value, [key]: event.target.checked })} />{key.replace("Enabled", "")}</label>)}</div>;
}

function TimeStep({ branches, tables, selectedTable, draft, setDraft, onHold, loading }: { branches: { id: string; name: string }[]; tables: Table[]; selectedTable?: Table; draft: { branchId: string; date: string; time: string; guests: number; tableId?: string }; setDraft: (value: Partial<typeof draft>) => void; onHold: () => void; loading: boolean }) {
  return <div className="step"><h2>Время и стол</h2><div className="form-grid two"><label className="field"><span>Филиал</span><select value={draft.branchId} onChange={(event) => setDraft({ branchId: event.target.value, tableId: undefined })}><option value="">Выберите филиал</option>{branches.map((branch) => <option value={branch.id} key={branch.id}>{branch.name}</option>)}</select></label><Field label="Дата" type="date" value={draft.date} onChange={(date) => setDraft({ date, tableId: undefined })} /><Field label="Время" type="time" value={draft.time} onChange={(time) => setDraft({ time, tableId: undefined })} /><label className="field"><span>Гости</span><select value={draft.guests} onChange={(event) => setDraft({ guests: Number(event.target.value), tableId: undefined })}>{[2,3,4,5,6,7,8].map((value) => <option value={value} key={value}>{value}</option>)}</select></label></div>{branches.length === 0 && <EmptyState label="Филиалы не найдены" description="Backend не вернул активные филиалы." />}{!draft.branchId && branches.length > 0 && <p className="copy">Выберите филиал, чтобы загрузить доступные столы.</p>}<div className="choice-grid">{tables.map((table) => <button className={`choice ${selectedTable?.id === table.id ? "active" : ""}`} onClick={() => setDraft({ tableId: table.id })} key={table.id}><strong>{table.name}</strong><span>до {table.capacity} гостей</span></button>)}</div>{draft.branchId && tables.length === 0 && <EmptyState label="Нет доступных столов" description="Попробуйте изменить время или количество гостей." />}<button className="primary" onClick={onHold} disabled={loading || !draft.branchId || !selectedTable}><Clock3 size={18} />{loading ? "Удерживаем" : "Удержать стол на 10 минут"}</button></div>;
}

function MixStep({ mixes, selectedMix, draft, setDraft, onSubmit, loading }: { mixes: Mix[]; selectedMix?: Mix; draft: { mixId: string; bowlId: string; comment: string; promocode: string }; setDraft: (value: Partial<typeof draft>) => void; onSubmit: () => void; loading: boolean }) {
  return <div className="step"><h2>Микс и оплата</h2><div className="choice-grid">{mixes.map((mix) => <button className={`choice mix ${selectedMix?.id === mix.id ? "active" : ""}`} onClick={() => setDraft({ mixId: mix.id, bowlId: mix.bowlId })} key={mix.id}><strong>{mix.name}</strong><span>{mix.tasteProfile} · {mix.strength} · {mix.price} ₽</span></button>)}</div>{mixes.length === 0 && <EmptyState label="Публичные миксы не найдены" description="Попросите персонал опубликовать миксы в CRM." />}<label className="field"><span>Комментарий</span><textarea value={draft.comment} onChange={(event) => setDraft({ comment: event.target.value })} placeholder="Средняя крепость, без сильного холода" /></label><Field label="Промокод" value={draft.promocode} onChange={(promocode) => setDraft({ promocode })} placeholder="HOOKAH20" /><button className="primary" onClick={onSubmit} disabled={loading || !selectedMix}><CreditCard size={18} />{loading ? "Создаем" : "Забронировать и оплатить"}</button></div>;
}

function PaymentStep({ paymentUrl, mode }: { paymentUrl: string; mode: "PROVIDER_REDIRECT" | "LOCAL_RETURN" }) {
  return <div className="success-step"><CheckCircle2 size={46} /><h2>Бронь создана</h2><p>{mode === "PROVIDER_REDIRECT" ? "Открываем страницу платежного провайдера. После webhook бронь станет CONFIRMED, клиент и менеджер получат уведомления." : "Провайдер оплаты не настроен, открыт локальный return flow для разработки. В production укажите Payments:CheckoutBaseUrl."}</p>{paymentUrl ? <a className="primary link-button" href={paymentUrl}>Перейти к оплате</a> : <p className="copy">Ссылка оплаты появится после создания платежа.</p>}</div>;
}

function HistoryStep({ clientId, bookings, reviews, rating, setRating, reviewText, setReviewText, onReview, onUpdateReview, onDeleteReview }: { clientId?: string; bookings: Booking[]; reviews: Review[]; rating: number; setRating: (value: number) => void; reviewText: string; setReviewText: (value: string) => void; onReview: () => void; onUpdateReview: (id: string, rating: number, text: string) => void; onDeleteReview: (id: string) => void }) {
  const ownReviews = clientId ? reviews.filter((review) => review.clientId === clientId) : [];
  return <div className="step"><h2>История и отзывы</h2><div className="history-list">{bookings.length === 0 ? <EmptyState label="История появится после первой брони" /> : bookings.map((booking) => <article key={booking.id}><History size={16} /><span>{timeLabel(booking.startTime)} · {booking.status}</span></article>)}</div><h3>Оставить отзыв</h3><div className="rating">{[1,2,3,4,5].map((value) => <button className={value <= rating ? "active" : ""} onClick={() => setRating(value)} key={value}><Star size={18} /></button>)}</div><label className="field"><span>Текст отзыва</span><textarea value={reviewText} onChange={(event) => setReviewText(event.target.value)} placeholder="Что понравилось в миксе и сервисе?" /></label><button className="primary" onClick={onReview}><MessageSquare size={18} />Отправить отзыв</button><div className="review-feed">{ownReviews.length === 0 ? <EmptyState label="Ваших отзывов пока нет" /> : ownReviews.map((review) => <EditableReview key={review.id} review={review} onSave={onUpdateReview} onDelete={onDeleteReview} />)}</div></div>;
}

function EditableReview({ review, onSave, onDelete }: { review: Review; onSave: (id: string, rating: number, text: string) => void; onDelete: (id: string) => void }) {
  const [rating, setRating] = useState(review.rating);
  const [text, setText] = useState(review.text ?? "");
  return <blockquote><div className="review-edit"><input type="number" min={1} max={5} value={rating} onChange={(event) => setRating(Number(event.target.value))} /><input value={text} onChange={(event) => setText(event.target.value)} /><button disabled={rating < 1 || rating > 5 || !text.trim()} onClick={() => onSave(review.id, rating, text)}>save</button><button className="danger" onClick={() => confirmDestructive("Удалить отзыв?", () => onDelete(review.id))}>delete</button></div></blockquote>;
}

function Field({ label, value, onChange, placeholder, type = "text" }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string; type?: string }) {
  return <label className="field"><span>{label}</span><input type={type} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} /></label>;
}

function SummaryLine({ label, value }: { label: string; value: string }) {
  return <div className="summary-line"><span>{label}</span><strong>{value}</strong></div>;
}

function confirmDestructive(message: string, action: () => void) {
  if (typeof window === "undefined" || window.confirm(message)) action();
}
