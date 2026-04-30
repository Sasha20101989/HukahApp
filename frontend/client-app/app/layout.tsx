import type { Metadata, Viewport } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Hookah Booking",
  description: "Client booking app for Hookah CRM Platform",
  manifest: "/manifest.webmanifest"
};

export const viewport: Viewport = {
  themeColor: "#1d7b65"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ru">
      <body>{children}</body>
    </html>
  );
}
