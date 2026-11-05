import { useEffect, useState } from "react";

export interface ToastMessage {
  id: number;
  text: string;
  isError: boolean;
}

const VISIBLE_MS = 4500;

/** Shows every pending notification at once; each expires on its own interval, so later feedback never waits. */
export function ToastStack({ messages, onDone }: { messages: readonly ToastMessage[]; onDone: (id: number) => void }) {
  // Only keyboard focus pauses expiry: a hover pause would keep the stack covering the controls
  // beneath it for as long as the pointer rests there.
  const [paused, setPaused] = useState(false);
  // Nothing renders without feedback: page-level alert/status roles mean visible feedback.
  // Each message carries its own live role (errors as alerts), so additions announce without
  // re-reading siblings. Keyboard entry lets readers scroll a flood while expiry is paused.
  if (messages.length === 0) return null;
  // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
  return <div className="toast-stack" role="region" aria-label="Notifications" tabIndex={0}
    onFocus={() => setPaused(true)} onBlur={event => {
      if (!event.currentTarget.contains(event.relatedTarget)) setPaused(false);
    }}>
    {messages.map(message => <Toast key={message.id} message={message} onDone={onDone} paused={paused} />)}
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
    <div className={`toast${message.isError ? " toast--error" : ""}`} role={message.isError ? "alert" : "status"}>
      {message.text}
    </div>
  );
}
