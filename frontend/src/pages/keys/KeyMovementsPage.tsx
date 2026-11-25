import { useState, useEffect } from 'react'
import { List, AlertCircle, RefreshCw } from 'lucide-react'
import client from '../../api/client'
import { APP_TIME_ZONE, addDays, localDateTimeToIso } from '../../utils/dateTime'
import Pagination, { DEFAULT_PAGE_SIZE } from '../../components/Pagination'

interface KeyMovement {
  id: string | number
  kulcsId?: number
  kulcsSzam?: string
  keyName?: string
  mozgasTipus?: string
  idopont?: string
  issuedAt?: string
  atvevoNev?: string
  userName?: string
  operatorNev?: string
  megjegyzes?: string
  teremAllapot?: string
  serulesLeiras?: string
}

function formatDate(isoString?: string): string {
  if (!isoString) return '—'
  try {
    const d = new Date(isoString)
    return d.toLocaleString('hu-HU', {
      timeZone: APP_TIME_ZONE,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return isoString
  }
}

interface KeyOption {
  id: number
  szam?: string
  teremNev?: string
}

export default function KeyMovementsPage() {
  const [movements, setMovements] = useState<KeyMovement[]>([])
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [reloadKey, setReloadKey] = useState(0)
  // Szűrés kulcsra (= egy kulcs előzményei), mozgástípusra és időszakra (budapesti napok).
  const [keys, setKeys] = useState<KeyOption[]>([])
  const [keyFilter, setKeyFilter] = useState('')
  const [typeFilter, setTypeFilter] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')

  const fetchMovements = () => {
    setIsLoading(true)
    setReloadKey((key) => key + 1)
  }

  useEffect(() => {
    let ignore = false
    client.get<KeyOption[]>('/keys')
      .then((response) => {
        if (!ignore && Array.isArray(response.data)) setKeys(response.data)
      })
      .catch(() => {})
    return () => {
      ignore = true
    }
  }, [])

  useEffect(() => {
    let ignore = false
    client
      .get<{ items?: KeyMovement[]; totalCount?: number } | KeyMovement[]>('/key-movements', {
        params: {
          page,
          pageSize: DEFAULT_PAGE_SIZE,
          kulcsId: keyFilter || undefined,
          mozgasTipus: typeFilter || undefined,
          tol: fromDate ? localDateTimeToIso(fromDate, '00:00') : undefined,
          ig: toDate ? localDateTimeToIso(addDays(toDate, 1), '00:00') : undefined,
        },
      })
      .then((r) => {
        if (!ignore) {
          const list = Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
          setMovements(list)
          setTotalCount(Array.isArray(r.data) ? list.length : (r.data?.totalCount ?? list.length))
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const axErr = err as { response?: { data?: { message?: string } } }
          setError(axErr.response?.data?.message || 'Nem sikerült betölteni a kulcsmozgásokat.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setIsLoading(false)
        }
      })
    return () => {
      ignore = true
    }
  }, [page, keyFilter, typeFilter, fromDate, toDate, reloadKey])

  const changeFilter = (setter: (value: string) => void) => (value: string) => {
    setter(value)
    setPage(1)
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <List size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Kulcsmozgások naplója</span>
          </h1>
          <p className="page-subtitle">A kulcsok kiadásának és visszavételének auditált története</p>
        </div>

        <button type="button" className="btn btn-secondary btn-sm" onClick={fetchMovements}>
          <RefreshCw size={13} strokeWidth={1.5} />
          <span>Frissítés</span>
        </button>
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginBottom: 16, alignItems: 'flex-end' }}>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="movements-key">Kulcs</label>
          <select id="movements-key" className="form-select" value={keyFilter}
            onChange={(e) => changeFilter(setKeyFilter)(e.target.value)}>
            <option value="">Minden kulcs</option>
            {keys.map((key) => (
              <option key={key.id} value={key.id}>{key.szam}{key.teremNev ? ` – ${key.teremNev}` : ''}</option>
            ))}
          </select>
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="movements-type">Mozgás</label>
          <select id="movements-type" className="form-select" value={typeFilter}
            onChange={(e) => changeFilter(setTypeFilter)(e.target.value)}>
            <option value="">Kiadás és visszavétel</option>
            <option value="Kiadás">Csak kiadás</option>
            <option value="Visszavétel">Csak visszavétel</option>
          </select>
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="movements-from">Naptól</label>
          <input id="movements-from" type="date" className="form-input" value={fromDate}
            onChange={(e) => changeFilter(setFromDate)(e.target.value)} />
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="movements-to">Napig</label>
          <input id="movements-to" type="date" className="form-input" min={fromDate || undefined} value={toDate}
            onChange={(e) => changeFilter(setToDate)(e.target.value)} />
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
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
                <th>Időpont</th>
                <th>Művelet</th>
                <th>Kulcs</th>
                <th>Átvevő személy</th>
                <th>Kiadó portás</th>
                <th>Terem állapota / sérülés</th>
                <th>Megjegyzés</th>
              </tr>
            </thead>
            <tbody>
              {movements.length === 0 ? (
                <tr>
                  <td colSpan={7} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Még nem történt kulcsmozgás a rendszerben.
                  </td>
                </tr>
              ) : (
                movements.map((m) => {
                  const isIssue = m.mozgasTipus === 'Kiadas' || m.mozgasTipus === 'Kiadás'
                  return (
                    <tr key={m.id}>
                      <td style={{ whiteSpace: 'nowrap', color: 'var(--color-gray-700)' }}>
                        {formatDate(m.idopont || m.issuedAt)}
                      </td>
                      <td>
                        {isIssue ? (
                          <span className="badge badge-warning">Kiadás</span>
                        ) : (
                          <span className="badge badge-success">Visszavétel</span>
                        )}
                      </td>
                      <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                        Kulcs #{m.kulcsSzam || m.kulcsId || m.keyName || m.id}
                      </td>
                      <td>{m.atvevoNev || m.userName || '—'}</td>
                      <td>{m.operatorNev || 'Porta'}</td>
                      <td style={{ color: 'var(--color-gray-600)', fontSize: 13 }}>
                        {m.teremAllapot || '—'}
                        {m.serulesLeiras && <div style={{ color: 'var(--color-danger)' }}>{m.serulesLeiras}</div>}
                      </td>
                      <td style={{ color: 'var(--color-gray-500)', fontSize: 13 }}>
                        {m.megjegyzes || '—'}
                      </td>
                    </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      )}
      <Pagination page={page} pageSize={DEFAULT_PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
