import { useState, useEffect } from 'react'
import { List, AlertCircle, RefreshCw } from 'lucide-react'
import client from '../../api/client'

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

export default function KeyMovementsPage() {
  const [movements, setMovements] = useState<KeyMovement[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')

  const fetchMovements = () => {
    setIsLoading(true)
    client
      .get<{ items?: KeyMovement[] } | KeyMovement[]>('/key-movements', {
        params: { page: 1, pageSize: 100 },
      })
      .then((r) => {
        const list = Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
        setMovements(list)
        setError('')
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a kulcsmozgásokat.')
      })
      .finally(() => {
        setIsLoading(false)
      })
  }

  useEffect(() => {
    let ignore = false
    client
      .get<{ items?: KeyMovement[] } | KeyMovement[]>('/key-movements', {
        params: { page: 1, pageSize: 100 },
      })
      .then((r) => {
        if (!ignore) {
          const list = Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
          setMovements(list)
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
  }, [])

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
    </div>
  )
}
