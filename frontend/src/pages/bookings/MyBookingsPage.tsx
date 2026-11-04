import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { CalendarDays, Trash2, Plus, AlertCircle } from 'lucide-react'
import client from '../../api/client'

interface Booking {
  id: string | number
  teremNev?: string
  roomName?: string
  room?: { name: string }
  startTime?: string
  endTime?: string
  date?: string
  status?: string
  note?: string
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

export default function MyBookingsPage() {
  const [bookings, setBookings] = useState<Booking[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    client
      .get<Booking[]>('/bookings/my', { signal: controller.signal })
      .then((r) => {
        setBookings(r.data)
        setIsLoading(false)
      })
      .catch((err: unknown) => {
        if ((err as { name?: string }).name !== 'CanceledError') {
          setError('Nem sikerült betölteni a foglalásokat.')
          setIsLoading(false)
        }
      })
    return () => controller.abort()
  }, [])

  const handleDelete = async (id: string | number) => {
    if (!window.confirm('Biztosan vissza szeretnéd vonni ezt a foglalást?')) return
    try {
      await client.delete(`/bookings/${id}`)
      setBookings((prev) => prev.filter((b) => b.id !== id))
    } catch {
      alert('A foglalás visszavonása sikertelen.')
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <CalendarDays size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Saját foglalásaim</span>
          </h1>
          <p className="page-subtitle">Az általad rögzített tantermi és laborfoglalások</p>
        </div>

        <Link to="/new-booking" className="btn btn-primary">
          <Plus size={15} strokeWidth={1.5} />
          <span>Új foglalás</span>
        </Link>
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
                <th>Dátum</th>
                <th>Idősáv</th>
                <th>Státusz</th>
                <th style={{ textAlign: 'right' }}>Művelet</th>
              </tr>
            </thead>
            <tbody>
              {bookings.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Jelenleg nincs aktív foglalásod.
                  </td>
                </tr>
              ) : (
                bookings.map((b) => {
                  const roomName = b.teremNev ?? b.roomName ?? b.room?.name ?? '—'
                  const dateStr = formatDate(b.startTime || b.date)
                  const timeRangeStr = formatTimeRange(b.startTime, b.endTime)
                  const isCancelable = b.status === 'Aktiv' || b.status === 'Active' || !b.status

                  return (
                    <tr key={b.id}>
                      <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>{roomName}</td>
                      <td>{dateStr}</td>
                      <td>{timeRangeStr}</td>
                      <td>{getStatusBadge(b.status)}</td>
                      <td style={{ textAlign: 'right' }}>
                        {isCancelable && (
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            style={{ color: 'var(--color-danger)' }}
                            onClick={() => handleDelete(b.id)}
                            title="Foglalás visszavonása"
                          >
                            <Trash2 size={14} strokeWidth={1.5} />
                            <span>Visszavonás</span>
                          </button>
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
