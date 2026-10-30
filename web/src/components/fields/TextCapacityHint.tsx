import { textCapacityError, textCapacityHint } from "../../lib/textCapacity";

export function TextCapacityHint({ id, text, capacity, compact }: { id: string; text: string; capacity: number; compact?: boolean }) {
  const error = textCapacityError(text, capacity);
  return <span id={id} aria-live="polite" aria-atomic="true" className={error ? "field__error text-capacity" : "field__hint text-capacity"}>
    {compact ? <><span aria-hidden="true">{textCapacityHint(text, capacity, true)}</span>
      <span className="visually-hidden">{textCapacityHint(text, capacity)}</span></> : textCapacityHint(text, capacity)}
  </span>;
}
