import { Orbitron, Rajdhani, Chakra_Petch } from "next/font/google";
import "./globals.css";

const orbitron = Orbitron({
  subsets: ["latin"],
  weight: ["500", "600", "700", "800", "900"],
  variable: "--font-display",
  display: "swap",
});

const rajdhani = Rajdhani({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-ui",
  display: "swap",
});

const chakra = Chakra_Petch({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-body",
  display: "swap",
});

export const metadata = {
  title: "PROJECT RIFT — Survive. Build. Raid. Dominate.",
  description:
    "Project Rift is a next-generation gaming network delivering premium multiplayer survival and roleplay experiences.",
  openGraph: {
    title: "PROJECT RIFT",
    description: "Survive. Build. Raid. Dominate.",
    type: "website",
  },
};

export const viewport = {
  themeColor: "#05070D",
};

export default function RootLayout({ children }) {
  return (
    <html lang="en">
      <body
        className={`${orbitron.variable} ${rajdhani.variable} ${chakra.variable}`}
      >
        {children}
      </body>
    </html>
  );
}
