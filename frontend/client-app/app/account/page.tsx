"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, Flame, History, LogOut, MessageSquare, Star, UserRound } from "lucide-react";
import { useEffect, useState } from "react";
import { configureApiAuth, deleteReview, getJson, getNotificationPreference, logoutAuth, timeLabel, updateClientProfile, updateNotificationPreference, updateReview, type Booking, type NotificationPreference, type Review } from "../../lib/api";
import { hydrateClientSession, useClientStore } from "../../lib/store";
import { EmptyState, FieldError, FormError, LoadingState, MutationToast } from "../../lib/ui";
import { isValidEmail } from "../../lib/validation";

export default function AccountPage() {
  const queryClient = useQueryClient();
  const { session, hydrated, setSession, logout } = useClientStore();
  const [notice, setNotice] = useState("");
  const [name, setName] = useState(session.name);
  const [email, setEmail] = useState(session.email);

  useEffect(() => hydrateClientSession(), []);
  useEffect(() => configureApiAuth({
    getRefreshToken: () => useClientStore.getState().session.refreshToken,
    onRefresh: setSession,
    onUnauthorized: logout
  }), [logout, setSession]);
  useEffect(() => {
    setName(session.name);
    setEmail(session.email);
  }, [session.email, session.name]);

  const authorized = async <T,>(call: (accessToken: string) => Promise<T>) => {
    if (!session.accessToken) throw new Error("Сессия клиента не найдена.");
    return call(session.accessToken);
  };

  const bookings = useQuery({
    queryKey: ["account-bookings", session.userId],
    enabled: Boolean(session.userId && session.accessToken),
    queryFn: () => authorized((accessToken) => getJson<Booking[]>("/api/bookings", accessToken))
  });
  const reviews = useQuery({
    queryKey: ["account-reviews"],
    enabled: Boolean(session.userId && session.accessToken),
    queryFn: () => getJson<Review[]>("/api/reviews")
  });
  const preferences = useQuery({
    queryKey: ["account-preferences", session.userId],
    enabled: Boolean(session.userId && session.accessToken),
    queryFn: () => authorized((accessToken) => getNotificationPreference(session.userId!, accessToken))
  });

  const saveProfile = useMutation({
    mutationFn: () => {
      if (!isValidEmail(email)) throw new Error("Email должен быть корректным.");
      return authorized((accessToken) => updateClientProfile(session.userId!, { name: name.trim(), email: email.trim() || undefined }, accessToken));
    },
    onSuccess: (profile) => {
      setSession({ name: profile.name, email: profile.email ?? "" });
      setNotice("Профиль сохранен.");
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось сохранить профиль")
  });
  const savePreferences = useMutation({
    mutationFn: (value: Omit<NotificationPreference, "userId">) => authorized((accessToken) => updateNotificationPreference(session.userId!, value, accessToken)),
    onSuccess: async () => {
      setNotice("Настройки уведомлений сохранены.");
      await queryClient.invalidateQueries({ queryKey: ["account-preferences"] });
    },
    onError: (error) => setNotice(error instanceof Error ? error.message : "Не удалось сохранить уведомления")
  });
  const editReview = useMutation({
    mutationFn: ({ id, rating, text }: { id: string; rating: number; text: string }) => authorized((accessToken) => updateReview(id, { rating, text }, accessToken)),
    onSuccess: async () => {
      setNotice("Отзыв обновлен.");
      await queryClient.invalidateQueries({ queryKey: ["account-reviews"] });
    }
  });
  const removeReview = useMutation({
    mutationFn: (id: string) => authorized((accessToken) => deleteReview(id, accessToken)),
    onSuccess: async () => {
      setNotice("Отзыв удален.");
      await queryClient.invalidateQueries({ queryKey: ["account-reviews"] });
    }
  });

  const handleLogout = async () => {
    if (session.refreshToken) {
      try { await logoutAuth(session.refreshToken); } catch { /* local logout still wins */ }
    }
    logout();
    window.location.href = "/";
  };

  if (!hydrated) return <main className="booking-shell"><section className="panel"><LoadingState label="Загружаем аккаунт..." /></section></main>;

  const ownReviews = session.userId ? (reviews.data ?? []).filter((review) => review.clientId === session.userId) : [];
  const preferenceValue = preferences.data
    ? { crmEnabled: preferences.data.crmEnabled, telegramEnabled: preferences.data.telegramEnabled, smsEnabled: preferences.data.smsEnabled, emailEnabled: preferences.data.emailEnabled, pushEnabled: preferences.data.pushEnabled }
    : { crmEnabled: true, telegramEnabled: true, smsEnabled: true, emailEnabled: true, pushEnabled: true };

  return <main className="booking-shell account-shell"><header className="app-header"><a className="logo link-button" href="/"><span className="logo-mark"><Flame size={20} /></span><span>Hookah Account</span></a><button className="logout" onClick={handleLogout}><LogOut size={15} />Выйти</button></header>{notice && <button className="notice" onClick={() => setNotice("")}>{notice}</button>}<MutationToast mutation={saveProfile} successMessage="Профиль сохранен" /><MutationToast mutation={savePreferences} successMessage="Настройки уведомлений сохранены" /><MutationToast mutation={editReview} successMessage="Отзыв обновлен" /><MutationToast mutation={removeReview} successMessage="Отзыв удален" />{(bookings.isError || preferences.isError || reviews.isError) && <FormError error={bookings.error ?? preferences.error ?? reviews.error} onRetry={() => queryClient.invalidateQueries()} />}<section className="account-grid"><article className="panel"><h2><UserRound size={22} />Профиль</h2><label className="field"><span>Имя</span><input value={name} onChange={(event) => setName(event.target.value)} /></label><label className="field"><span>Email</span><input type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="client@mail.com" /></label><FieldError message={!isValidEmail(email) ? "Email должен быть корректным." : ""} /><button className="primary" disabled={!name.trim() || !isValidEmail(email) || saveProfile.isPending} onClick={() => saveProfile.mutate()}>Сохранить профиль</button></article><article className="panel"><h2><Bell size={22} />Уведомления</h2><div className="preference-card compact-preferences">{(["crmEnabled", "telegramEnabled", "smsEnabled", "emailEnabled", "pushEnabled"] as const).map((key) => <label key={key}><input type="checkbox" checked={preferenceValue[key]} onChange={(event) => savePreferences.mutate({ ...preferenceValue, [key]: event.target.checked })} />{key.replace("Enabled", "")}</label>)}</div></article><article className="panel"><h2><History size={22} />Брони</h2><div className="history-list">{bookings.isLoading ? <LoadingState label="Загружаем историю..." /> : (bookings.data ?? []).length === 0 ? <EmptyState label="История бронирований пустая" /> : (bookings.data ?? []).map((booking) => <article key={booking.id}><History size={16} /><span>{timeLabel(booking.startTime)} · {booking.status} · {booking.depositAmount} ₽</span></article>)}</div></article><article className="panel"><h2><MessageSquare size={22} />Отзывы</h2><div className="review-feed">{ownReviews.length === 0 ? <EmptyState label="Отзывов пока нет" /> : ownReviews.map((review) => <AccountReview key={review.id} review={review} onSave={(rating, text) => editReview.mutate({ id: review.id, rating, text })} onDelete={() => removeReview.mutate(review.id)} />)}</div></article></section></main>;
}

function AccountReview({ review, onSave, onDelete }: { review: Review; onSave: (rating: number, text: string) => void; onDelete: () => void }) {
  const [rating, setRating] = useState(review.rating);
  const [text, setText] = useState(review.text ?? "");
  return <blockquote><div className="review-edit"><input type="number" min={1} max={5} value={rating} onChange={(event) => setRating(Number(event.target.value))} /><input value={text} onChange={(event) => setText(event.target.value)} /><button disabled={rating < 1 || rating > 5 || !text.trim()} onClick={() => onSave(rating, text)}><Star size={14} />save</button><button className="danger" onClick={() => confirmDestructive("Удалить отзыв?", onDelete)}>delete</button></div></blockquote>;
}

function confirmDestructive(message: string, action: () => void) {
  if (typeof window === "undefined" || window.confirm(message)) action();
}
