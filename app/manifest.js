// PWA manifest — gives Android / Chrome / installed-app icons for all devices.
export default function manifest() {
  return {
    name: "PROJECT RIFT",
    short_name: "RIFT",
    description:
      "Project Rift is a next-generation gaming network delivering premium multiplayer survival and roleplay experiences.",
    start_url: "/",
    display: "standalone",
    background_color: "#05070D",
    theme_color: "#05070D",
    icons: [
      {
        src: "/icons/icon-192.png",
        sizes: "192x192",
        type: "image/png",
        purpose: "any",
      },
      {
        src: "/icons/icon-512.png",
        sizes: "512x512",
        type: "image/png",
        purpose: "any",
      },
    ],
  };
}
