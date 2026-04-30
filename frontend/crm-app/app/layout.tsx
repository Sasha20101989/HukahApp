import type { Metadata, Viewport } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Hookah CRM",
  description: "Operational CRM workspace for hookah venues",
  manifest: "/manifest.webmanifest"
};

export const viewport: Viewport = {
  themeColor: "#1f7a66"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ru">
      <body>{children}</body>
    </html>
  );
}
