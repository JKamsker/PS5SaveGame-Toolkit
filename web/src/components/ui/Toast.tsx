import { useEffect } from "react";

export interface ToastMessage {
  id: number;
  text: string;
  isError: boolean;
}

const VISIBLE_MS = 4500;

/** Shows every pending notification at once; each expires on its own interval, so later feedback never waits. */
export function ToastStack({ messages, onDone }: { messages: readonly ToastMessage[]; onDone: (id: number) => void }) {
  if (messages.length === 0) return null;
  return <div className="toast-stack">{messages.map(message => <Toast key={message.id} message={message} onDone={onDone} />)}</div>;
}

function Toast({ message, onDone }: { message: ToastMessage; onDone: (id: number) => void }) {
  useEffect(() => {
    const timer = window.setTimeout(() => onDone(message.id), message.isError ? VISIBLE_MS * 2 : VISIBLE_MS);
    return () => window.clearTimeout(timer);
  }, [message, onDone]);

  return (
    <div className={`toast${message.isError ? " toast--error" : ""}`} role={message.isError ? "alert" : "status"}>
      {message.text}
    </div>
  );
}
