import { useState, useEffect } from 'react'
import { CalendarDays, Search, AlertCircle } from 'lucide-react'
import client from '../../api/client'

interface Booking {
  id: string | number
  teremNev?: string
  roomName?: string
  felhasznaloNev?: string
  felhasznaloEmail?: string
  userName?: string
  startTime?: string
  endTime?: string
  date?: string
  status?: string
  specialisIgenyek?: string
}

function formatDate(isoString?: string): string {
  if (!isoString) return '—'
  try {
    const d = new Date(isoString)
    return d.toLocaleDateString('hu-HU', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    })
  } catch {
    return isoString
  }
}

function formatTimeRange(start?: string, end?: string): string {
  if (!start) return '—'
  try {
    const dStart = new Date(start)
    const startStr = dStart.toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' })
    if (!end) return startStr
    const dEnd = new Date(end)
    const endStr = dEnd.toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' })
    return `${startStr} – ${endStr}`
  } catch {
    return `${start} – ${end || ''}`
  }
}

function getStatusBadge(status: string | undefined) {
  switch (status) {
    case 'Aktiv':
    case 'Active':
    case 'Aktív':
      return <span className="badge badge-success">Aktív</span>
    case 'Kesz':
    case 'Completed':
      return <span className="badge badge-info">Lezárult</span>
    case 'Megszuntetve':
    case 'Cancelled':
    case 'Lemondva':
      return <span className="badge badge-danger">Lemondva</span>
    case 'NoShow':
      return <span className="badge badge-warning">No-Show</span>
    default:
      return <span className="badge badge-neutral">{status ?? '—'}</span>
  }
}

export default function BookingsPage() {
  const [bookings, setBookings] = useState<Booking[]>([])
  const [search, setSearch] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    client
      .get<{ items?: Booking[] } | Booking[]>('/bookings', {
        params: { page: 1, pageSize: 100 },
      })
      .then((r) => {
        const list = Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
        setBookings(list)
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a foglalásokat.')
      })
      .finally(() => setIsLoading(false))
  }, [])

  const filtered = bookings.filter((b) => {
    const q = search.toLowerCase()
    const room = (b.teremNev ?? b.roomName ?? '').toLowerCase()
    const user = (b.felhasznaloNev ?? b.userName ?? '').toLowerCase()
    return room.includes(q) || user.includes(q)
  })

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <CalendarDays size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Összes teremfoglalás</span>
          </h1>
          <p className="page-subtitle">Az intézményben rögzített tantermi idősávok áttekintése</p>
        </div>

        <div className="search-bar">
          <Search size={15} color="var(--color-gray-400)" strokeWidth={1.5} />
          <input
            type="text"
            placeholder="Keresés terem vagy oktató szerint…"
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
                <th>Terem</th>
                <th>Lefoglaló oktató</th>
                <th>Dátum</th>
                <th>Idősáv</th>
                <th>Speciális igények</th>
                <th>Státusz</th>
              </tr>
            </thead>
            <tbody>
              {filtered.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Nincs a feltételeknek megfelelő foglalás.
                  </td>
                </tr>
              ) : (
                filtered.map((b) => (
                  <tr key={b.id}>
                    <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                      {b.teremNev ?? b.roomName ?? '—'}
                    </td>
                    <td>{b.felhasznaloNev ?? b.userName ?? '—'}</td>
                    <td>{formatDate(b.startTime || b.date)}</td>
                    <td>{formatTimeRange(b.startTime, b.endTime)}</td>
                    <td style={{ color: 'var(--color-gray-600)', fontSize: 13 }}>
                      {b.specialisIgenyek ? (
                        <span style={{ background: 'var(--color-gray-100)', padding: '2px 8px', borderRadius: 4 }}>
                          {b.specialisIgenyek}
                        </span>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td>{getStatusBadge(b.status)}</td>
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
