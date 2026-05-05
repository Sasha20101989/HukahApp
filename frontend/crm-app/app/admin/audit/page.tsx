"use client";

import { useQuery } from "@tanstack/react-query";
import { Flame, History, LogOut, ShieldCheck } from "lucide-react";
import type { ReactNode } from "react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { configureApiAuth, getAuditLogs, getJson, logoutAuth, shortId, type AuditLog, type UserProfile } from "../../../lib/api";
import { hydrateCrmSession, useCrmStore } from "../../../lib/store";
import { EmptyState, FormError, FormField, LoadingState } from "../../../lib/ui";

export default function AuditLogPage() {
  const { hydrated, session, setAuth, setProfile, logout } = useCrmStore();
  const token = session.accessToken;
  const role = session.profile?.role;
  const [action, setAction] = useState("");
  const [targetType, setTargetType] = useState("");
  const [actorUserId, setActorUserId] = useState("");
  const [limit, setLimit] = useState(100);

  useEffect(() => hydrateCrmSession(), []);
  useEffect(() => configureApiAuth({
    getRefreshToken: () => useCrmStore.getState().session.refreshToken,
    onRefresh: setAuth,
    onUnauthorized: logout
  }), [logout, setAuth]);

  const authorized = useCallback(async <T,>(call: (accessToken: string) => Promise<T>): Promise<T> => {
    if (!token) throw new Error("Сессия не найдена.");
    return call(token);
  }, [token]);

  const profile = useQuery({ queryKey: ["audit-admin", "me", token], enabled: Boolean(token), queryFn: () => authorized((accessToken) => getJson<UserProfile>("/api/users/me", accessToken)), retry: false });
  useEffect(() => { if (profile.data) setProfile(profile.data); }, [profile.data, setProfile]);

  const canReadAudit = role === "OWNER";
  const logs = useQuery({
    queryKey: ["audit-logs", action, targetType, actorUserId, limit],
    enabled: Boolean(token && canReadAudit),
    queryFn: () => authorized((accessToken) => getAuditLogs({ action: action.trim() || undefined, targetType: targetType.trim() || undefined, actorUserId: actorUserId.trim() || undefined, limit }, accessToken))
  });
  const actionOptions = useMemo(() => unique(logs.data?.map((item) => item.action) ?? []), [logs.data]);
  const targetOptions = useMemo(() => unique(logs.data?.map((item) => item.targetType) ?? []), [logs.data]);

  const handleLogout = useCallback(async () => {
    if (session.refreshToken) {
      try { await logoutAuth(session.refreshToken); } catch { /* local logout still wins */ }
    }
    logout();
  }, [logout, session.refreshToken]);

  if (!hydrated) return <AuditShell><LoadingState label="Загружаем сессию..." /></AuditShell>;
  if (!token || profile.isError) return <AuditShell><FormError error={profile.error} message={profile.error instanceof Error ? profile.error.message : "Нужна авторизация OWNER."} /><a className="primary" href="/login">Войти</a></AuditShell>;
  if (!role) return <AuditShell><LoadingState label="Проверяем профиль..." /></AuditShell>;
  if (!canReadAudit) return <AuditShell><FormError message="Audit log доступен только OWNER." /><a className="primary" href="/">Вернуться в CRM</a></AuditShell>;

  return <main className="shell"><aside className="sidebar"><div className="brand"><span className="brand-mark"><Flame size={20} /></span><span>Hookah CRM</span></div><div className="role-card"><span><ShieldCheck size={15} /> Admin console</span><strong>{session.profile?.name}</strong><small>{role}</small><button className="ghost" onClick={handleLogout}><LogOut size={15} />Выйти</button></div><nav className="nav" aria-label="Admin sections"><a className="admin-link" href="/">CRM</a><a className="admin-link" href="/admin/tenants">Тенанты</a><a className="admin-link" href="/admin/roles">Роли</a><a className="admin-link" href="/admin/audit"><History size={18} />Audit</a></nav></aside><section className="main"><header className="topbar"><div className="title"><h1>Audit Log</h1><p>Tenant-scoped журнал чувствительных действий: auth, роли, настройки, склад, возвраты.</p></div></header><section className="section"><div className="section-head"><h2>События</h2><span className="meta">последние {limit}</span></div><div className="form-strip"><FormField label="Action"><input list="audit-actions" value={action} onChange={(event) => setAction(event.target.value)} placeholder="role.create" /></FormField><FormField label="Target"><input list="audit-targets" value={targetType} onChange={(event) => setTargetType(event.target.value)} placeholder="role" /></FormField><FormField label="Actor user"><input value={actorUserId} onChange={(event) => setActorUserId(event.target.value)} placeholder="uuid" /></FormField><FormField label="Limit"><input type="number" min={1} max={500} value={limit} onChange={(event) => setLimit(Number(event.target.value))} /></FormField><datalist id="audit-actions">{actionOptions.map((value) => <option value={value} key={value} />)}</datalist><datalist id="audit-targets">{targetOptions.map((value) => <option value={value} key={value} />)}</datalist></div>{logs.isLoading && <LoadingState label="Загружаем audit log..." />}{logs.isError && <FormError error={logs.error} />}{!logs.isLoading && !logs.isError && (logs.data?.length ? <div className="queue cards">{logs.data.map((item) => <AuditRow key={item.id} log={item} />)}</div> : <EmptyState label="Audit log пуст" description="События появятся после чувствительных операций." />)}</section></section></main>;
}

function AuditShell({ children }: { children: ReactNode }) {
  return <main className="auth-shell"><section className="auth-card"><div className="brand"><span className="brand-mark"><History size={20} /></span><span>Audit Log</span></div>{children}</section></main>;
}

function AuditRow({ log }: { log: AuditLog }) {
  return <article className="booking-row booking-row-rich"><div><strong>{log.action}</strong><div className="meta">{new Date(log.createdAt).toLocaleString("ru-RU")} · {log.result}</div><div className="pill-row"><span>{log.targetType}</span>{log.targetId && <span>{shortId(log.targetId)}</span>}{log.actorUserId && <span>actor {shortId(log.actorUserId)}</span>}</div></div><div className="audit-metadata"><span className="meta">correlation {log.correlationId ? shortId(log.correlationId) : "-"}</span>{log.metadataJson && <pre>{formatMetadata(log.metadataJson)}</pre>}</div></article>;
}

function formatMetadata(value: string) {
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}

function unique(values: string[]) {
  return Array.from(new Set(values.filter(Boolean))).sort();
}
