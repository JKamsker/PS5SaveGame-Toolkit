import { useEffect, useState } from "react";

export interface ToastMessage {
  id: number;
  text: string;
  isError: boolean;
}

const VISIBLE_MS = 4500;
const ANNOUNCEMENT_DELAY_MS = 100;

/** Shows every pending notification at once; each expires on its own interval, so later feedback never waits. */
export function ToastStack({ messages, onDone }: { messages: readonly ToastMessage[]; onDone: (id: number) => void }) {
  // Only keyboard focus pauses expiry: a hover pause would keep the stack covering the controls
  // beneath it for as long as the pointer rests there.
  const [paused, setPaused] = useState(false);
  return <>
    <ToastAnnouncements messages={messages} />
    {messages.length > 0 && <VisibleToasts messages={messages} onDone={onDone} paused={paused} setPaused={setPaused} />}
  </>;
}

function ToastAnnouncements({ messages }: { messages: readonly ToastMessage[] }) {
  const [announced, setAnnounced] = useState<readonly ToastMessage[]>([]);
  useEffect(() => {
    // MDN Live regions: establish an empty region, then update in a later task.
    // Stable message keys announce additions without rereading surviving siblings.
    const timer = window.setTimeout(() => setAnnounced(messages), ANNOUNCEMENT_DELAY_MS);
    return () => window.clearTimeout(timer);
  }, [messages]);
  return <>{([false, true] as const).map(isError =>
    <div key={String(isError)} className="visually-hidden" aria-live={isError ? "assertive" : "polite"}
      aria-atomic="false" aria-relevant="additions text">
      {announced.filter(message => message.isError === isError).map(message => <div key={message.id}>{message.text}</div>)}
    </div>)}
  </>;
}

function VisibleToasts({ messages, onDone, paused, setPaused }: {
  messages: readonly ToastMessage[]; onDone: (id: number) => void; paused: boolean; setPaused: (paused: boolean) => void;
}) {
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
    <div className={`toast${message.isError ? " toast--error" : ""}`} role={message.isError ? "alert" : "status"} aria-live="off">
      {message.text}
    </div>
  );
}
