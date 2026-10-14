import { useEffect, useRef } from 'react';

/** An inline choice: keyboard focus enters it and returns to the initiating control. */
export function PromotionChoices({ choose, cancel, disabled = false }: { choose: (piece: string) => void; cancel: () => void; disabled?: boolean }) {
  const first = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const trigger = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    first.current?.focus();
    return () => { if (trigger?.isConnected) trigger.focus(); };
  }, []);
  return <fieldset className="promotion" onKeyDown={event => {
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); cancel(); }
  }}>
    <legend>Promote your pawn to</legend>
    {([['q', 'Queen'], ['r', 'Rook'], ['b', 'Bishop'], ['n', 'Knight']] as const).map(([piece, label], index) =>
      <button ref={index === 0 ? first : undefined} key={piece} disabled={disabled} onClick={() => choose(piece)}>{label}</button>)}
    <button onClick={cancel}>Cancel promotion</button>
  </fieldset>;
}
