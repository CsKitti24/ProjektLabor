import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { CalendarDays, Trash2, Plus, AlertCircle, CalendarClock, Check } from 'lucide-react'
import client from '../../api/client'
import { APP_TIME_ZONE } from '../../utils/dateTime'
import { useConfirm } from '../../components/confirmContext'
import Pagination, { DEFAULT_PAGE_SIZE } from '../../components/Pagination'
import RescheduleBookingModal, { type ReschedulableBooking } from '../../components/RescheduleBookingModal'

interface Booking {
  id: string | number
  teremId?: number
  teremNev?: string
  roomName?: string
  room?: { name: string }
  startTime?: string
  endTime?: string
  date?: string
  status?: string
  specialisIgenyek?: string
  note?: string
}

function formatDate(isoString?: string): string {
  if (!isoString) return '—'
  try {
    const d = new Date(isoString)
    return d.toLocaleDateString('hu-HU', {
      timeZone: APP_TIME_ZONE,
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
    const startStr = dStart.toLocaleTimeString('hu-HU', { timeZone: APP_TIME_ZONE, hour: '2-digit', minute: '2-digit' })
    if (!end) return startStr
    const dEnd = new Date(end)
    const endStr = dEnd.toLocaleTimeString('hu-HU', { timeZone: APP_TIME_ZONE, hour: '2-digit', minute: '2-digit' })
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
  const confirm = useConfirm()
  const [bookings, setBookings] = useState<Booking[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  // A betöltés pillanata: ehhez képest döntjük el, mely foglalás kezdődött már el.
  const [loadedAt, setLoadedAt] = useState(0)
  // Lapozás: a backend a legújabbakat adja elöl, az összes foglalás számát az X-Total-Count fejlécben.
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [reloadKey, setReloadKey] = useState(0)
  const [rescheduling, setRescheduling] = useState<ReschedulableBooking | null>(null)
  const [successMsg, setSuccessMsg] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    client
      .get<Booking[]>('/bookings/my', { signal: controller.signal, params: { page, pageSize: DEFAULT_PAGE_SIZE } })
      .then((r) => {
        setBookings(r.data)
        const total = Number(r.headers?.['x-total-count'])
        setTotalCount(Number.isFinite(total) ? total : r.data.length)
        setLoadedAt(Date.now())
        setIsLoading(false)
      })
      .catch((err: unknown) => {
        if ((err as { name?: string }).name !== 'CanceledError') {
          setError('Nem sikerült betölteni a foglalásokat.')
          setIsLoading(false)
        }
      })
    return () => controller.abort()
  }, [page, reloadKey])

  const handleDelete = async (id: string | number) => {
    if (!(await confirm({ title: 'Foglalás lemondása', message: 'Biztosan lemondod ezt a foglalást?', confirmLabel: 'Lemondás', danger: true }))) return
    try {
      await client.delete(`/bookings/${id}`)
      // A lemondott foglalás nem tűnik el: "Megszüntetve" állapotban marad a listában, ahogy újratöltés után is.
      setBookings((prev) => prev.map((b) => (b.id === id ? { ...b, status: 'Megszuntetve' } : b)))
      setError('')
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A foglalás visszavonása sikertelen.')
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

      {successMsg && (
        <div className="alert alert-success" role="status">
          <Check size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{successMsg}</span>
        </div>
      )}

      {rescheduling && (
        <RescheduleBookingModal
          booking={rescheduling}
          allowRoomChange={false}
          onClose={() => setRescheduling(null)}
          onSaved={() => {
            setRescheduling(null)
            setReloadKey((key) => key + 1)
            setSuccessMsg('A foglalás áthelyezve.')
            setTimeout(() => setSuccessMsg(''), 4000)
          }}
        />
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
                <th>Speciális igények</th>
                <th>Státusz</th>
                <th style={{ textAlign: 'right' }}>Művelet</th>
              </tr>
            </thead>
            <tbody>
              {bookings.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Jelenleg nincs aktív foglalásod.
                  </td>
                </tr>
              ) : (
                bookings.map((b) => {
                  const roomName = b.teremNev ?? b.roomName ?? b.room?.name ?? '—'
                  const dateStr = formatDate(b.startTime || b.date)
                  const timeRangeStr = formatTimeRange(b.startTime, b.endTime)
                  // Megkezdett foglalást oktató nem mondhat le (a backend is elutasítja), ezért a gomb sem jelenik meg.
                  const hasStarted = b.startTime ? new Date(b.startTime).getTime() <= loadedAt : false
                  const isCancelable = (b.status === 'Aktiv' || b.status === 'Active' || !b.status) && !hasStarted

                  return (
                    <tr key={b.id}>
                      <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>{roomName}</td>
                      <td>{dateStr}</td>
                      <td>{timeRangeStr}</td>
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
                      <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                        {isCancelable && (
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            onClick={() => setRescheduling({ ...b, teremNev: roomName })}
                            aria-label={`${roomName} ${dateStr} foglalás áthelyezése`}
                            title="Foglalás áthelyezése másik időpontra"
                          >
                            <CalendarClock size={14} strokeWidth={1.5} />
                            <span>Áthelyezés</span>
                          </button>
                        )}
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
      <Pagination page={page} pageSize={DEFAULT_PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
