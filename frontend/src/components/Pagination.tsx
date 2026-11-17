interface PaginationProps {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

export const DEFAULT_PAGE_SIZE = 50

/** Egyszerű lapozó: csak akkor jelenik meg, ha az elemek nem férnek el egy oldalon. */
export default function Pagination({ page, pageSize, totalCount, onPageChange }: PaginationProps) {
  if (totalCount <= pageSize) return null

  const pageCount = Math.ceil(totalCount / pageSize)
  const from = (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, totalCount)

  return (
    <nav
      aria-label="Lapozás"
      style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 12 }}
    >
      <span style={{ fontSize: 12.5, color: 'var(--color-gray-500)' }}>
        {from}–{to} / {totalCount}
      </span>
      <div style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          disabled={page <= 1}
          onClick={() => onPageChange(page - 1)}
        >
          Előző
        </button>
        <span style={{ fontSize: 12.5, color: 'var(--color-gray-600)' }}>
          {page} / {pageCount}
        </span>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          disabled={page >= pageCount}
          onClick={() => onPageChange(page + 1)}
        >
          Következő
        </button>
      </div>
    </nav>
  )
}
