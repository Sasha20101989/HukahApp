export function normalizePhoneInput(value: string) {
  const digits = value.trim().replace(/\D/g, "").slice(0, 15);
  if (!digits) return "";
  if (digits.startsWith("8")) return `+7${digits.slice(1)}`;
  return `+${digits}`;
}

export function normalizePhone(value: string) {
  return normalizePhoneInput(value);
}

export function isValidPhone(value: string) {
  return /^\+\d{10,15}$/.test(normalizePhone(value));
}

export function isValidEmail(value: string) {
  const email = value.trim();
  return !email || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
}
