import Image from "next/image";
import Particles from "@/components/Particles";
import BlastDoors from "@/components/BlastDoors";
import "./loading-screen.css";

export const metadata = {
  title: "Connecting to PROJECT RIFT...",
  description: "Loading into the server.",
};

export default function LoadingScreen() {
  return (
    <main className="loading-screen">
      {/* Cinematic Blast Doors on page load */}
      <BlastDoors />

      {/* Background Image with slow pan/zoom animation */}
      <div className="ls-bg-wrapper">
        <Image
          src="/loading.png"
          alt="Background"
          fill
          priority
          sizes="100vw"
          className="ls-bg-image"
        />
      </div>

      {/* Overlay to darken the background slightly */}
      <div className="ls-overlay" />

      {/* Particles effect */}
      <div className="ls-particles">
        <Particles count={35} />
      </div>

      {/* Foreground Content */}
      <div className="ls-content">
        <div className="ls-logo-wrapper">
          {/* Counter-rotating dashed ring */}
          <span className="ls-logo-ring" />
          <Image
            src="/logo.png"
            alt="Project Rift Logo"
            width={140}
            height={140}
            className="ls-logo-img"
            priority
          />
        </div>
        <h1 className="ls-title">
          PROJECT <span>RIFT</span>
        </h1>
        <p className="ls-subtitle">PREPARING TO DEPLOY...</p>
        
        {/* Simple loading bar animation */}
        <div className="ls-loading-bar-container">
          <div className="ls-loading-bar-fill" />
        </div>
      </div>
    </main>
  );
}
