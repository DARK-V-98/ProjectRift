"use client";
import { useState, useEffect } from "react";

const messages = [
  { title: "SERVER RULE", text: "No cheating, scripting, or exploiting of any kind. Instant ban." },
  { title: "SERVER RULE", text: "Maximum team limit is 4. No alliances or sharing bases." },
  { title: "SERVER RULE", text: "Respect all players. Zero tolerance for racism or extreme toxicity." },
  { title: "SURVIVAL TIP", text: "Use the '/kit' command in chat to claim your starter pack." },
  { title: "COMMUNITY", text: "Join our Discord at discord.gg/projectrift to read rules and get support." },
  { title: "WIPE SCHEDULE", text: "Map wipes every Thursday at 2:00 PM EST. Blueprints wipe monthly." }
];

export default function MessageBoard() {
  const [index, setIndex] = useState(0);
  const [visible, setVisible] = useState(true);

  useEffect(() => {
    const interval = setInterval(() => {
      setVisible(false);
      setTimeout(() => {
        setIndex((prev) => (prev + 1) % messages.length);
        setVisible(true);
      }, 400); // Matches CSS transition time
    }, 4500);

    return () => clearInterval(interval);
  }, []);

  const current = messages[index];

  return (
    <div className="ls-board">
      <div className="ls-board-outline">
        {/* Decorative corner lines */}
        <div className="ls-board-corner top-left" />
        <div className="ls-board-corner top-right" />
        <div className="ls-board-corner bottom-left" />
        <div className="ls-board-corner bottom-right" />
        
        <div className={`ls-board-body ${visible ? "visible" : "hidden"}`}>
          <span className="ls-board-tag">{current.title}</span>
          <p className="ls-board-text">{current.text}</p>
        </div>
      </div>
    </div>
  );
}
