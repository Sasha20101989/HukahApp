import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Hookah Booking",
    short_name: "Booking",
    description: "Client booking app for Hookah Place",
    start_url: "/",
    display: "standalone",
    background_color: "#edf5f1",
    theme_color: "#1d7b65",
    icons: [
      {
        src: "/icon.svg",
        sizes: "any",
        type: "image/svg+xml"
      }
    ]
  };
}
