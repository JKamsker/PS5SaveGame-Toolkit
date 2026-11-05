import { useEffect, useState } from "react";

export interface ToastMessage {
  id: number;
  text: string;
  isError: boolean;
}

const VISIBLE_MS = 4500;

/** Shows every pending notification at once; each expires on its own interval, so later feedback never waits. */
export function ToastStack({ messages, onDone }: { messages: readonly ToastMessage[]; onDone: (id: number) => void }) {
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const paused = hovered || focused;
  // Prime both priorities before feedback arrives. Announce only additions, so adding
  // or expiring a sibling does not read the entire stack again.
  // Keyboard entry lets readers scroll a flood while expiry is paused.
  // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
  return <div className="toast-stack" role="region" aria-label="Notifications" tabIndex={messages.length ? 0 : -1}
    onMouseEnter={() => setHovered(true)} onMouseLeave={() => setHovered(false)}
    onFocus={() => setFocused(true)} onBlur={event => {
      if (!event.currentTarget.contains(event.relatedTarget)) setFocused(false);
    }}>
    <div className="toast-stack__group" role="status" aria-atomic="false" aria-relevant="additions">
      {messages.filter(message => !message.isError).map(message => <Toast key={message.id} message={message} onDone={onDone} paused={paused} />)}
    </div>
    <div className="toast-stack__group" role="alert" aria-atomic="false" aria-relevant="additions">
      {messages.filter(message => message.isError).map(message => <Toast key={message.id} message={message} onDone={onDone} paused={paused} />)}
    </div>
  </div>;
}

function Toast({ message, onDone, paused }: { message: ToastMessage; onDone: (id: number) => void; paused: boolean }) {
  useEffect(() => {
    // Give the reader a full interval after leaving the scrollable stack.
    if (paused) return;
    const timer = window.setTimeout(() => onDone(message.id), message.isError ? VISIBLE_MS * 2 : VISIBLE_MS);
    return () => window.clearTimeout(timer);
  }, [message, onDone, paused]);

  return (
    <div className={`toast${message.isError ? " toast--error" : ""}`}>
      {message.text}
    </div>
  );
}
