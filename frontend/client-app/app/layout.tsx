import type { Metadata, Viewport } from "next";
import "./globals.css";
import { Providers } from "./providers";

export const metadata: Metadata = {
  title: "Hookah Booking",
  description: "Client booking app for Hookah CRM Platform",
  manifest: "/manifest.webmanifest"
};

export const viewport: Viewport = {
  themeColor: "#172f29"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ru">
      <body><Providers>{children}</Providers></body>
    </html>
  );
}
