import { useState, useEffect } from 'react'
import { Key, Search, AlertCircle } from 'lucide-react'
import client from '../../api/client'

interface KeyItem {
  id: number | string
  szam?: string
  name?: string
  identifier?: string
  tipus?: string
  teremNev?: string
  roomName?: string
  jelenlegiAllapot?: string
  isAvailable?: boolean
  isOut?: boolean
  aktivalva?: boolean
}

export default function KeysPage() {
  const [keys, setKeys] = useState<KeyItem[]>([])
  const [search, setSearch] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    client
      .get<KeyItem[]>('/keys')
      .then((r) => setKeys(r.data))
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a kulcsokat.')
      })
      .finally(() => setIsLoading(false))
  }, [])

  const filtered = keys.filter((k) => {
    const q = search.toLowerCase()
    const keyNum = (k.szam ?? k.name ?? k.identifier ?? '').toLowerCase()
    const room = (k.teremNev ?? k.roomName ?? '').toLowerCase()
    return keyNum.includes(q) || room.includes(q)
  })

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <Key size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Kulcsok nyilvántartása</span>
          </h1>
          <p className="page-subtitle">A rendszerben lévő teremkulcsok és mesterkulcsok státusza</p>
        </div>

        <div className="search-bar">
          <Search size={15} color="var(--color-gray-400)" strokeWidth={1.5} />
          <input
            type="text"
            placeholder="Keresés kulcsszám vagy terem szerint…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
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
                <th>Kulcsszám</th>
                <th>Típus</th>
                <th>Hozzárendelt terem</th>
                <th>Jelenlegi státusz</th>
                <th>Engedélyezve</th>
              </tr>
            </thead>
            <tbody>
              {filtered.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Nincs a keresésnek megfelelő kulcs.
                  </td>
                </tr>
              ) : (
                filtered.map((k) => {
                  const isOut =
                    k.jelenlegiAllapot === 'Kiadva' || k.isOut === true || k.isAvailable === false
                  const isDeactivated =
                    k.jelenlegiAllapot === 'Deaktivalt' || k.aktivalva === false

                  return (
                    <tr key={k.id}>
                      <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                        Kulcs #{k.szam ?? k.name ?? k.identifier ?? k.id}
                      </td>
                      <td>
                        <span className="badge badge-neutral">{k.tipus || 'Osztálykulcs'}</span>
                      </td>
                      <td>{k.teremNev ?? k.roomName ?? '—'}</td>
                      <td>
                        {isDeactivated ? (
                          <span className="badge badge-danger">Deaktivált</span>
                        ) : isOut ? (
                          <span className="badge badge-warning">Kiadva</span>
                        ) : (
                          <span className="badge badge-success">Szabadon elérhető</span>
                        )}
                      </td>
                      <td>
                        {k.aktivalva !== false ? (
                          <span className="badge badge-success">Igen</span>
                        ) : (
                          <span className="badge badge-neutral">Nem</span>
                        )}
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
