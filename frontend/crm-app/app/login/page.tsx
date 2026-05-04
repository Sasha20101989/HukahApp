"use client";

import { useMutation } from "@tanstack/react-query";
import { Flame, LockKeyhole } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { loginStaff } from "../../lib/api";
import { useCrmStore } from "../../lib/store";
import { FieldError, FormError, MutationToast } from "../../lib/ui";
import { isValidPhone, normalizePhone, normalizePhoneInput } from "../../lib/validation";

export default function CrmLoginPage() {
  const router = useRouter();
  const setAuth = useCrmStore((state) => state.setAuth);
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const phoneError = phone && !isValidPhone(phone) ? "Телефон должен быть в формате +79990000000." : "";
  const mutation = useMutation({
    mutationFn: () => loginStaff(normalizePhone(phone), password),
    onSuccess: (auth) => {
      setAuth(auth);
      router.replace("/");
    }
  });

  return <main className="auth-shell"><section className="auth-card"><div className="brand"><span className="brand-mark"><LockKeyhole size={20} /></span><span>Hookah CRM</span></div><h1>Вход персонала</h1><p>Введите телефон и пароль сотрудника. Доступ к разделам ограничивается ролью и permission matrix.</p><label className="field"><span>Телефон</span><input type="tel" inputMode="tel" value={phone} onChange={(event) => setPhone(normalizePhoneInput(event.target.value))} placeholder="+79990000000" /></label><FieldError message={phoneError} /><label className="field"><span>Пароль</span><input type="password" value={password} onChange={(event) => setPassword(event.target.value)} /></label><button className="primary" onClick={() => mutation.mutate()} disabled={mutation.isPending || !isValidPhone(phone) || !password}><Flame size={18} />{mutation.isPending ? "Входим" : "Войти"}</button>{mutation.isError && <FormError error={mutation.error} message="Не удалось войти" />}{mutation.isPending && <MutationToast mutation={mutation} pendingMessage="Входим..." />}</section></main>;
}
