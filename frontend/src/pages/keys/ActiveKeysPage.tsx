import { useState, useEffect } from 'react'
import { Key, AlertCircle, RefreshCw, Clock } from 'lucide-react'
import client from '../../api/client'

interface KeyItem {
  id: number | string
  szam?: string
  tipus?: string
  teremNev?: string
  jelenlegiAllapot?: string
  aktivalva?: boolean
}

interface OverdueKey {
  kulcsId: number
  kulcsSzam: string
  teremNev: string
  kiadasIdeje: string
  birtokosNev?: string
  birtokosEmail?: string
  késésPercekben: number
}

export default function ActiveKeysPage() {
  const [keys, setKeys] = useState<KeyItem[]>([])
  const [overdueKeys, setOverdueKeys] = useState<OverdueKey[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')

  const fetchData = () => {
    setIsLoading(true)
    Promise.all([
      client.get<KeyItem[]>('/keys'),
      client.get<OverdueKey[]>('/key-movements/overdue').catch(() => ({ data: [] })),
    ])
      .then(([keysRes, overdueRes]) => {
        setKeys(keysRes.data)
        setOverdueKeys(overdueRes.data)
        setError('')
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni az aktív kulcsokat.')
      })
      .finally(() => {
        setIsLoading(false)
      })
  }

  useEffect(() => {
    fetchData()
  }, [])

  const issuedKeys = keys.filter((k) => k.jelenlegiAllapot === 'Kiadva')

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <Key size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Kint lévő aktív kulcsok</span>
          </h1>
          <p className="page-subtitle">A portáról jelenleg kiadott tantermi és mesterkulcsok</p>
        </div>

        <button type="button" className="btn btn-secondary btn-sm" onClick={fetchData}>
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

      {/* Késedelmes kulcsok figyelmeztető doboz (ha van) */}
      {overdueKeys.length > 0 && (
        <div className="alert alert-error" style={{ marginBottom: 20 }}>
          <Clock size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>
            <strong>Figyelem:</strong> {overdueKeys.length} db kulcs leadási határideje lejárt!
          </span>
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
                <th>Kulcsszám</th>
                <th>Típus</th>
                <th>Hozzárendelt terem</th>
                <th>Állapot</th>
              </tr>
            </thead>
            <tbody>
              {issuedKeys.length === 0 ? (
                <tr>
                  <td colSpan={4} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Jelenleg egyetlen kulcs sincs kint, minden kulcs a helyén a portán.
                  </td>
                </tr>
              ) : (
                issuedKeys.map((k) => (
                  <tr key={k.id}>
                    <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                      Kulcs #{k.szam || k.id}
                    </td>
                    <td>
                      <span className="badge badge-neutral">{k.tipus || 'Osztálykulcs'}</span>
                    </td>
                    <td>{k.teremNev || '—'}</td>
                    <td>
                      <span className="badge badge-warning">Kiadva</span>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
