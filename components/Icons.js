// Lightweight inline SVG icons — no external icon dependency.
export const Icon = {
  rift: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path
        d="M12 2 4 7v10l8 5 8-5V7l-8-5Z"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinejoin="round"
      />
      <path d="M12 7v10M8 9l8 6M16 9l-8 6" stroke="currentColor" strokeWidth="1.4" />
    </svg>
  ),
  discord: (p) => (
    <svg viewBox="0 0 24 24" fill="currentColor" {...p}>
      <path d="M19.3 5.3A16 16 0 0 0 15.4 4l-.2.4a14.8 14.8 0 0 1 3.5 1.1 13.6 13.6 0 0 0-11.4 0A14.8 14.8 0 0 1 10.8 4.4L10.6 4a16 16 0 0 0-3.9 1.3C4.2 9 3.5 12.7 3.8 16.3a16.1 16.1 0 0 0 4.9 2.5l.6-1a10.5 10.5 0 0 1-1.7-.8l.4-.3a11.5 11.5 0 0 0 9.8 0l.4.3a10.5 10.5 0 0 1-1.7.8l.6 1a16 16 0 0 0 4.9-2.5c.4-4.2-.6-7.9-2.8-11Zm-9.4 9c-.9 0-1.7-.9-1.7-1.9s.8-1.9 1.7-1.9 1.7.9 1.7 1.9-.7 1.9-1.7 1.9Zm6.2 0c-.9 0-1.7-.9-1.7-1.9s.8-1.9 1.7-1.9 1.7.9 1.7 1.9-.7 1.9-1.7 1.9Z" />
    </svg>
  ),
  server: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <rect x="3" y="4" width="18" height="6" rx="2" stroke="currentColor" strokeWidth="1.6" />
      <rect x="3" y="14" width="18" height="6" rx="2" stroke="currentColor" strokeWidth="1.6" />
      <circle cx="7" cy="7" r="1" fill="currentColor" />
      <circle cx="7" cy="17" r="1" fill="currentColor" />
    </svg>
  ),
  players: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <circle cx="9" cy="8" r="3.2" stroke="currentColor" strokeWidth="1.6" />
      <path d="M3.5 19a5.5 5.5 0 0 1 11 0" stroke="currentColor" strokeWidth="1.6" />
      <path d="M16 6.2a3 3 0 0 1 0 5.6M17.5 19a5.2 5.2 0 0 0-2.3-4.3" stroke="currentColor" strokeWidth="1.6" />
    </svg>
  ),
  chat: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M4 5h16v11H8l-4 3V5Z" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" />
    </svg>
  ),
  pulse: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M3 12h4l2-6 4 12 2-6h6" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  bolt: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M13 2 4 14h7l-1 8 9-12h-7l1-8Z" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" />
    </svg>
  ),
  shield: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M12 2 4 5v6c0 5 3.5 8.5 8 11 4.5-2.5 8-6 8-11V5l-8-3Z" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" />
      <path d="m9 12 2 2 4-4" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  scale: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M12 3v18M5 7h14M7 7l-3 7h6l-3-7Zm10 0-3 7h6l-3-7Z" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
    </svg>
  ),
  cube: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M12 2 3 7v10l9 5 9-5V7l-9-5Z" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
      <path d="m3 7 9 5 9-5M12 12v10" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
    </svg>
  ),
  users: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <circle cx="12" cy="8" r="3.4" stroke="currentColor" strokeWidth="1.6" />
      <path d="M5 20a7 7 0 0 1 14 0" stroke="currentColor" strokeWidth="1.6" />
    </svg>
  ),
  arrow: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M5 12h14m-6-6 6 6-6 6" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  menu: (p) => (
    <svg viewBox="0 0 24 24" fill="none" {...p}>
      <path d="M4 7h16M4 12h16M4 17h16" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  ),
  x: (p) => (
    <svg viewBox="0 0 24 24" fill="currentColor" {...p}>
      <path d="M18.9 2H22l-7.6 8.7L23 22h-6.6l-5.1-6.7L5.5 22H2.4l8.1-9.3L1.5 2h6.8l4.6 6.1L18.9 2Zm-1.2 18h1.7L7.2 3.8H5.4L17.7 20Z" />
    </svg>
  ),
  youtube: (p) => (
    <svg viewBox="0 0 24 24" fill="currentColor" {...p}>
      <path d="M22 8.2a3 3 0 0 0-2.1-2.1C18 5.6 12 5.6 12 5.6s-6 0-7.9.5A3 3 0 0 0 2 8.2 31 31 0 0 0 1.6 12 31 31 0 0 0 2 15.8a3 3 0 0 0 2.1 2.1c1.9.5 7.9.5 7.9.5s6 0 7.9-.5a3 3 0 0 0 2.1-2.1A31 31 0 0 0 22.4 12 31 31 0 0 0 22 8.2ZM10 15V9l5.2 3L10 15Z" />
    </svg>
  ),
  twitch: (p) => (
    <svg viewBox="0 0 24 24" fill="currentColor" {...p}>
      <path d="M4 2 3 6v13h4v3h3l3-3h4l5-5V2H4Zm16 9-3 3h-4l-3 3v-3H7V4h13v7ZM15 7h-2v5h2V7Zm-5 0H8v5h2V7Z" />
    </svg>
  ),
};
