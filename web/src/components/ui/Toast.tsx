import { useEffect } from "react";

export interface ToastMessage {
  id: number;
  text: string;
  isError: boolean;
}

const VISIBLE_MS = 4500;

/** Shows every pending notification at once; each expires on its own interval, so later feedback never waits. */
export function ToastStack({ messages, onDone }: { messages: readonly ToastMessage[]; onDone: (id: number) => void }) {
  // Prime both priorities before feedback arrives. Announce only additions, so adding
  // or expiring a sibling does not read the entire stack again.
  return <div className="toast-stack">
    <div className="toast-stack__group" role="status" aria-atomic="false" aria-relevant="additions">
      {messages.filter(message => !message.isError).map(message => <Toast key={message.id} message={message} onDone={onDone} />)}
    </div>
    <div className="toast-stack__group" role="alert" aria-atomic="false" aria-relevant="additions">
      {messages.filter(message => message.isError).map(message => <Toast key={message.id} message={message} onDone={onDone} />)}
    </div>
  </div>;
}

function Toast({ message, onDone }: { message: ToastMessage; onDone: (id: number) => void }) {
  useEffect(() => {
    const timer = window.setTimeout(() => onDone(message.id), message.isError ? VISIBLE_MS * 2 : VISIBLE_MS);
    return () => window.clearTimeout(timer);
  }, [message, onDone]);

  return (
    <div className={`toast${message.isError ? " toast--error" : ""}`}>
      {message.text}
    </div>
  );
}
