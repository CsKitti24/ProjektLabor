import { Fragment, useEffect, useState, type FormEvent } from 'react'
import { AlertCircle, ClipboardList, RefreshCw, Search } from 'lucide-react'
import client from '../../api/client'
import Pagination, { DEFAULT_PAGE_SIZE } from '../../components/Pagination'

interface AuditLog {
  id: number
  felhasznaloId?: number
  felhasznaloNev?: string
  muveletTipus: string
  entitasTipus: string
  entitasId?: string
  regiErtek?: string | null
  ujErtek?: string | null
  indoklas?: string | null
  letrehozasIdeje: string
}

interface AuditResponse {
  items: AuditLog[]
  totalCount: number
}

interface AuditFilters {
  muveletTipus: string
  entitasTipus: string
  kezdet: string
  veg: string
}

const EMPTY_FILTERS: AuditFilters = { muveletTipus: '', entitasTipus: '', kezdet: '', veg: '' }

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('hu-HU', { dateStyle: 'medium', timeStyle: 'short' })
}

// A szűrő napjai helyi naptári napok: a kezdet a nap eleje, a vég a nap vége (UTC ISO-ban küldve).
function toQueryParams(filters: AuditFilters, page: number) {
  const params: Record<string, string | number> = { page, pageSize: DEFAULT_PAGE_SIZE }
  if (filters.muveletTipus.trim()) params.muveletTipus = filters.muveletTipus.trim()
  if (filters.entitasTipus.trim()) params.entitasTipus = filters.entitasTipus.trim()
  if (filters.kezdet) params.kezdet = new Date(`${filters.kezdet}T00:00:00`).toISOString()
  if (filters.veg) params.veg = new Date(`${filters.veg}T23:59:59`).toISOString()
  return params
}

export default function AuditLogPage() {
  const [logs, setLogs] = useState<AuditLog[]>([])
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [draftFilters, setDraftFilters] = useState<AuditFilters>(EMPTY_FILTERS)
  const [filters, setFilters] = useState<AuditFilters>(EMPTY_FILTERS)
  const [expandedId, setExpandedId] = useState<number | null>(null)

  useEffect(() => {
    let ignore = false
    client
      .get<AuditResponse>('/audit', { params: toQueryParams(filters, page) })
      .then((response) => {
        if (!ignore) {
          setLogs(response.data.items ?? [])
          setTotalCount(response.data.totalCount ?? response.data.items?.length ?? 0)
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const axErr = err as { response?: { data?: { message?: string } } }
          setError(axErr.response?.data?.message || 'Az auditnapló betöltése sikertelen.')
        }
      })
      .finally(() => {
        if (!ignore) setIsLoading(false)
      })
    return () => {
      ignore = true
    }
  }, [reload, page, filters])

  const applyFilters = (event: FormEvent) => {
    event.preventDefault()
    if (draftFilters.kezdet && draftFilters.veg && draftFilters.kezdet > draftFilters.veg) {
      setError('A kezdő dátum nem lehet későbbi a záró dátumnál.')
      return
    }
    setExpandedId(null)
    setPage(1)
    setFilters(draftFilters)
  }

  const clearFilters = () => {
    setDraftFilters(EMPTY_FILTERS)
    setExpandedId(null)
    setPage(1)
    setFilters(EMPTY_FILTERS)
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <ClipboardList size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Auditnapló</span>
          </h1>
          <p className="page-subtitle">A rendszerben végrehajtott műveletek és hozzáférések részletes, nem módosítható naplója</p>
        </div>
        <button type="button" className="btn btn-secondary btn-sm" onClick={() => setReload((value) => value + 1)} disabled={isLoading}>
          <RefreshCw size={14} strokeWidth={1.5} />
          Frissítés
        </button>
      </div>

      <form
        onSubmit={applyFilters}
        aria-label="Auditnapló szűrése"
        style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 10, alignItems: 'end', marginBottom: 16 }}
      >
        <div>
          <label className="form-label" htmlFor="audit-muvelet">Művelet</label>
          <input id="audit-muvelet" className="form-input" placeholder="pl. ACCESS_FORBIDDEN"
            value={draftFilters.muveletTipus}
            onChange={(e) => setDraftFilters((current) => ({ ...current, muveletTipus: e.target.value }))} />
        </div>
        <div>
          <label className="form-label" htmlFor="audit-entitas">Entitás</label>
          <input id="audit-entitas" className="form-input" placeholder="pl. Foglalas"
            value={draftFilters.entitasTipus}
            onChange={(e) => setDraftFilters((current) => ({ ...current, entitasTipus: e.target.value }))} />
        </div>
        <div>
          <label className="form-label" htmlFor="audit-kezdet">Kezdő nap</label>
          <input id="audit-kezdet" className="form-input" type="date"
            value={draftFilters.kezdet}
            onChange={(e) => setDraftFilters((current) => ({ ...current, kezdet: e.target.value }))} />
        </div>
        <div>
          <label className="form-label" htmlFor="audit-veg">Záró nap</label>
          <input id="audit-veg" className="form-input" type="date"
            value={draftFilters.veg}
            onChange={(e) => setDraftFilters((current) => ({ ...current, veg: e.target.value }))} />
        </div>
        <div style={{ display: 'inline-flex', gap: 6 }}>
          <button type="submit" className="btn btn-primary btn-sm">
            <Search size={14} strokeWidth={1.5} />
            Szűrés
          </button>
          <button type="button" className="btn btn-secondary btn-sm" onClick={clearFilters}>Törlés</button>
        </div>
      </form>

      {error && (
        <div className="alert alert-error" role="alert">
          <AlertCircle size={16} strokeWidth={1.5} />
          <span>{error}</span>
        </div>
      )}

      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : (
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Dátum</th>
                <th>Felhasználó</th>
                <th>Művelet</th>
                <th>Érintett entitás</th>
                <th style={{ textAlign: 'right' }}>Részletek</th>
              </tr>
            </thead>
            <tbody>
              {logs.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', padding: 36 }}>Nincs megjeleníthető esemény.</td></tr>
              ) : logs.map((log) => {
                const hasDetails = Boolean(log.regiErtek || log.ujErtek || log.indoklas)
                const isExpanded = expandedId === log.id
                return (
                  <Fragment key={log.id}>
                    <tr>
                      <td>{formatDate(log.letrehozasIdeje)}</td>
                      <td>{log.felhasznaloNev || (log.felhasznaloId ? `#${log.felhasznaloId}` : 'Rendszer')}</td>
                      <td>{log.muveletTipus}</td>
                      <td>{log.entitasTipus}{log.entitasId ? ` #${log.entitasId}` : ''}</td>
                      <td style={{ textAlign: 'right' }}>
                        {hasDetails ? (
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            aria-expanded={isExpanded}
                            onClick={() => setExpandedId(isExpanded ? null : log.id)}
                          >
                            {isExpanded ? 'Bezárás' : 'Megnyitás'}
                          </button>
                        ) : (
                          <span style={{ color: 'var(--color-gray-400)' }}>—</span>
                        )}
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr>
                        <td colSpan={5} style={{ background: 'var(--color-gray-50)' }}>
                          <dl style={{ display: 'grid', gridTemplateColumns: 'max-content 1fr', gap: '4px 12px', margin: 0, fontSize: 13 }}>
                            {log.regiErtek && (<><dt style={{ fontWeight: 600 }}>Régi érték</dt><dd style={{ margin: 0, wordBreak: 'break-word' }}>{log.regiErtek}</dd></>)}
                            {log.ujErtek && (<><dt style={{ fontWeight: 600 }}>Új érték</dt><dd style={{ margin: 0, wordBreak: 'break-word' }}>{log.ujErtek}</dd></>)}
                            {log.indoklas && (<><dt style={{ fontWeight: 600 }}>Indoklás</dt><dd style={{ margin: 0, wordBreak: 'break-word' }}>{log.indoklas}</dd></>)}
                          </dl>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
      <Pagination page={page} pageSize={DEFAULT_PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
