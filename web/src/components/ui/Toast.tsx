import { useEffect } from "react";

export interface ToastMessage {
  text: string;
  isError: boolean;
  at: number;
}

const VISIBLE_MS = 4500;

export function Toast({ message, onDone }: { message: ToastMessage; onDone: () => void }) {
  useEffect(() => {
    const timer = window.setTimeout(onDone, message.isError ? VISIBLE_MS * 2 : VISIBLE_MS);
    return () => window.clearTimeout(timer);
  }, [message, onDone]);

  return (
    <div className={`toast${message.isError ? " toast--error" : ""}`} role={message.isError ? "alert" : "status"}>
      {message.text}
    </div>
  );
}
