import { useEffect, useState } from 'react'
import { AlertCircle, ClipboardList, RefreshCw } from 'lucide-react'
import client from '../../api/client'

interface AuditLog {
  id: number
  felhasznaloId?: number
  felhasznaloNev?: string
  muveletTipus: string
  entitasTipus: string
  entitasId?: string
  letrehozasIdeje: string
}

interface AuditResponse {
  items: AuditLog[]
  totalCount: number
}

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('hu-HU', { dateStyle: 'medium', timeStyle: 'short' })
}

export default function AuditLogPage() {
  const [logs, setLogs] = useState<AuditLog[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let ignore = false
    client
      .get<AuditResponse>('/audit?page=1&pageSize=100')
      .then((response) => {
        if (!ignore) {
          setLogs(response.data.items ?? [])
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
  }, [reload])

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <ClipboardList size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Auditnapló</span>
          </h1>
          <p className="page-subtitle">A rendszerben végrehajtott műveletek időrendi áttekintése</p>
        </div>
        <button type="button" className="btn btn-secondary btn-sm" onClick={() => setReload((value) => value + 1)} disabled={isLoading}>
          <RefreshCw size={14} strokeWidth={1.5} />
          Frissítés
        </button>
      </div>

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
              </tr>
            </thead>
            <tbody>
              {logs.length === 0 ? (
                <tr><td colSpan={4} style={{ textAlign: 'center', padding: 36 }}>Nincs megjeleníthető esemény.</td></tr>
              ) : logs.map((log) => (
                <tr key={log.id}>
                  <td>{formatDate(log.letrehozasIdeje)}</td>
                  <td>{log.felhasznaloNev || (log.felhasznaloId ? `#${log.felhasznaloId}` : 'Rendszer')}</td>
                  <td>{log.muveletTipus}</td>
                  <td>{log.entitasTipus}{log.entitasId ? ` #${log.entitasId}` : ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
