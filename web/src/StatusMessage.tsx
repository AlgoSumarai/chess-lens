import type { HTMLAttributes } from 'react';

// Keep changing text in a paragraph beneath the status region. Orca/Chromium
// otherwise treats changes directly on the native status-bar node as silent UI updates.
export function StatusMessage({ children, className = '', ...attributes }: HTMLAttributes<HTMLDivElement>) {
  return <div {...attributes} className={`status-message ${className}`} role="status" aria-live="polite" aria-atomic="true"><p>{children}</p></div>;
}
