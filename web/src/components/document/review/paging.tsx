import { useState } from "react";

const PAGE_SIZE = 100;

/** Bound the DOM even when a bounded server response still contains thousands of changes. */
export function useReviewPage<T>(items: readonly T[]) {
  const [requestedPage, setPage] = useState(0);
  const pages = Math.max(1, Math.ceil(items.length / PAGE_SIZE));
  const page = Math.min(requestedPage, pages - 1);
  return { items: items.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE), total: items.length, page, pages, setPage };
}

export function ReviewPager({ page, pages, setPage, label }: { page: number; pages: number; setPage: (page: number) => void; label: string }) {
  if (pages <= 1) return null;
  return <nav className="review-pager" aria-label={label}>
    <button type="button" className="btn btn--small" disabled={page === 0} onClick={() => setPage(page - 1)}>Previous</button>
    <span aria-live="polite">Page {page + 1} of {pages}</span>
    <button type="button" className="btn btn--small" disabled={page === pages - 1} onClick={() => setPage(page + 1)}>Next</button>
  </nav>;
}
