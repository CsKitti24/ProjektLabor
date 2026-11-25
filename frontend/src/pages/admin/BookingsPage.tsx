import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { CalendarDays, AlertCircle, PlusCircle, Check } from 'lucide-react'
import client from '../../api/client'
import { fetchAllUsers, type UserListItem } from '../../api/users'
import { useAuth } from '../../auth/AuthContext'
import Pagination, { DEFAULT_PAGE_SIZE } from '../../components/Pagination'
import RescheduleBookingModal, { type ReschedulableBooking } from '../../components/RescheduleBookingModal'
import { useConfirm } from '../../components/confirmContext'
import { addDays, formatDateOnly, formatTimeOnly, localDateInputValue, localDateTimeToIso } from '../../utils/dateTime'

interface Booking {
  id: number
  teremId?: number
  teremNev?: string
  felhasznaloId?: number
  felhasznaloNev?: string
  startTime?: string
  endTime?: string
  status?: string
  specialisIgenyek?: string
}

interface RoomOption {
  id: number
  nev?: string
}

const STATUS_OPTIONS = [
  { value: 'Aktiv', label: 'Aktív' },
  { value: 'Kesz', label: 'Lezárult' },
  { value: 'Megszuntetve', label: 'Lemondva' },
  { value: 'NoShow', label: 'No-Show' },
]

function getStatusBadge(status: string | undefined) {
  switch (status) {
    case 'Aktiv':
      return <span className="badge badge-success">Aktív</span>
    case 'Kesz':
      return <span className="badge badge-info">Lezárult</span>
    case 'Megszuntetve':
      return <span className="badge badge-danger">Lemondva</span>
    case 'NoShow':
      return <span className="badge badge-warning">No-Show</span>
    default:
      return <span className="badge badge-neutral">{status ?? '—'}</span>
  }
}

function errorMessage(err: unknown, fallback: string): string {
  const axErr = err as { response?: { data?: { message?: string } } }
  return axErr.response?.data?.message || fallback
}

/**
 * Foglalások áttekintése. Az adminisztrátor lemondhat és áthelyezhet; a portás és az üzemeltetési
 * igazgató csak megtekint (a portás alapból a mai napot látja, hogy tudja, ki jön kulcsért).
 */
export default function BookingsPage() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Adminisztrator'
  const confirm = useConfirm()
  const today = localDateInputValue()

  const [bookings, setBookings] = useState<Booking[]>([])
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [successMsg, setSuccessMsg] = useState('')
  const [reloadKey, setReloadKey] = useState(0)
  const [rescheduling, setRescheduling] = useState<ReschedulableBooking | null>(null)
  // A betöltés pillanata: ehhez képest döntjük el, mely foglalás kezdődött már el.
  const [loadedAt, setLoadedAt] = useState(0)

  const [rooms, setRooms] = useState<RoomOption[]>([])
  const [instructors, setInstructors] = useState<UserListItem[]>([])
  const [roomFilter, setRoomFilter] = useState('')
  const [instructorFilter, setInstructorFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [fromDate, setFromDate] = useState(() => (user?.role === 'Portas' ? today : ''))
  const [toDate, setToDate] = useState(() => (user?.role === 'Portas' ? today : ''))

  useEffect(() => {
    let ignore = false
    client.get<RoomOption[]>('/rooms')
      .then((response) => {
        if (!ignore && Array.isArray(response.data)) setRooms(response.data)
      })
      .catch(() => {})
    if (isAdmin) {
      fetchAllUsers({ role: 'Oktato' })
        .then((list) => {
          if (!ignore) setInstructors([...list].sort((a, b) => (a.nev || a.username).localeCompare(b.nev || b.username, 'hu')))
        })
        .catch(() => {})
    }
    return () => {
      ignore = true
    }
  }, [isAdmin])

  // A szűrést a backend végzi (nem csak az aktuális oldalon), a dátumok budapesti napként értendők.
  useEffect(() => {
    let ignore = false
    client
      .get<{ items?: Booking[]; totalCount?: number }>('/bookings', {
        params: {
          page,
          pageSize: DEFAULT_PAGE_SIZE,
          teremId: roomFilter || undefined,
          felhasznaloId: instructorFilter || undefined,
          status: statusFilter || undefined,
          tol: fromDate ? localDateTimeToIso(fromDate, '00:00') : undefined,
          ig: toDate ? localDateTimeToIso(addDays(toDate, 1), '00:00') : undefined,
        },
      })
      .then((r) => {
        if (ignore) return
        const list = r.data?.items ?? []
        setBookings(list)
        setTotalCount(r.data?.totalCount ?? list.length)
        setLoadedAt(Date.now())
        setError('')
      })
      .catch((err: unknown) => {
        if (!ignore) setError(errorMessage(err, 'Nem sikerült betölteni a foglalásokat.'))
      })
      .finally(() => {
        if (!ignore) setIsLoading(false)
      })
    return () => {
      ignore = true
    }
  }, [page, roomFilter, instructorFilter, statusFilter, fromDate, toDate, reloadKey])

  const showSuccess = (message: string) => {
    setError('')
    setSuccessMsg(message)
    setTimeout(() => setSuccessMsg(''), 4000)
  }

  const changeFilter = (setter: (value: string) => void) => (value: string) => {
    setter(value)
    setPage(1)
  }

  const cancelBooking = async (booking: Booking) => {
    const description = `${booking.teremNev ?? ''}, ${formatDateOnly(booking.startTime)} ${formatTimeOnly(booking.startTime)}`
    if (!(await confirm({
      title: 'Foglalás lemondása',
      message: `Lemondod ${booking.felhasznaloNev ?? 'az oktató'} foglalását (${description})? Az oktató értesítést kap.`,
      confirmLabel: 'Lemondás',
      danger: true,
    }))) return

    try {
      await client.delete(`/bookings/${booking.id}`)
      setBookings((prev) => prev.map((item) => (item.id === booking.id ? { ...item, status: 'Megszuntetve' } : item)))
      showSuccess('A foglalás lemondva, az oktató értesítést kapott.')
    } catch (err: unknown) {
      setError(errorMessage(err, 'A foglalás lemondása nem sikerült.'))
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <CalendarDays size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>{user?.role === 'Portas' ? 'Foglalások (mai nap)' : 'Összes teremfoglalás'}</span>
          </h1>
          <p className="page-subtitle">Az intézményben rögzített tantermi idősávok áttekintése</p>
        </div>

        {isAdmin && (
          <Link to="/new-booking" className="btn btn-primary btn-sm">
            <PlusCircle size={14} strokeWidth={1.5} />
            <span>Foglalás oktató nevében</span>
          </Link>
        )}
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginBottom: 16, alignItems: 'flex-end' }}>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="bookings-from">Naptól</label>
          <input id="bookings-from" type="date" className="form-input" value={fromDate}
            onChange={(e) => changeFilter(setFromDate)(e.target.value)} />
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="bookings-to">Napig</label>
          <input id="bookings-to" type="date" className="form-input" min={fromDate || undefined} value={toDate}
            onChange={(e) => changeFilter(setToDate)(e.target.value)} />
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="bookings-room">Terem</label>
          <select id="bookings-room" className="form-select" value={roomFilter}
            onChange={(e) => changeFilter(setRoomFilter)(e.target.value)}>
            <option value="">Minden terem</option>
            {rooms.map((room) => <option key={room.id} value={room.id}>{room.nev}</option>)}
          </select>
        </div>
        {isAdmin && (
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label className="form-label" htmlFor="bookings-instructor">Oktató</label>
            <select id="bookings-instructor" className="form-select" value={instructorFilter}
              onChange={(e) => changeFilter(setInstructorFilter)(e.target.value)}>
              <option value="">Minden oktató</option>
              {instructors.map((instructor) => (
                <option key={instructor.id} value={instructor.id}>{instructor.nev || instructor.username}</option>
              ))}
            </select>
          </div>
        )}
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="bookings-status">Állapot</label>
          <select id="bookings-status" className="form-select" value={statusFilter}
            onChange={(e) => changeFilter(setStatusFilter)(e.target.value)}>
            <option value="">Minden állapot</option>
            {STATUS_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </div>
      </div>

      {error && (
        <div className="alert alert-error" role="alert">
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
          allowRoomChange
          onClose={() => setRescheduling(null)}
          onSaved={() => {
            setRescheduling(null)
            setReloadKey((key) => key + 1)
            showSuccess('A foglalás áthelyezve, az oktató értesítést kapott.')
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
                <th>Lefoglaló oktató</th>
                <th>Dátum</th>
                <th>Idősáv</th>
                <th>Speciális igények</th>
                <th>Státusz</th>
                {isAdmin && <th style={{ textAlign: 'right' }}>Művelet</th>}
              </tr>
            </thead>
            <tbody>
              {bookings.length === 0 ? (
                <tr>
                  <td colSpan={isAdmin ? 7 : 6} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Nincs a feltételeknek megfelelő foglalás.
                  </td>
                </tr>
              ) : (
                bookings.map((b) => {
                  const isActive = b.status === 'Aktiv'
                  const hasStarted = b.startTime ? new Date(b.startTime).getTime() <= loadedAt : true
                  return (
                    <tr key={b.id}>
                      <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>{b.teremNev ?? '—'}</td>
                      <td>{b.felhasznaloNev ?? '—'}</td>
                      <td>{formatDateOnly(b.startTime)}</td>
                      <td>{formatTimeOnly(b.startTime)} – {formatTimeOnly(b.endTime)}</td>
                      <td style={{ color: 'var(--color-gray-600)', fontSize: 13 }}>
                        {b.specialisIgenyek ? (
                          <span style={{ background: 'var(--color-gray-100)', padding: '2px 8px', borderRadius: 4 }}>
                            {b.specialisIgenyek}
                          </span>
                        ) : '—'}
                      </td>
                      <td>{getStatusBadge(b.status)}</td>
                      {isAdmin && (
                        <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                          {isActive && !hasStarted && (
                            <button type="button" className="btn btn-ghost btn-sm"
                              aria-label={`#${b.id} foglalás áthelyezése`}
                              onClick={() => setRescheduling(b)}>
                              Áthelyezés
                            </button>
                          )}
                          {isActive && (
                            <button type="button" className="btn btn-ghost btn-sm" style={{ color: 'var(--color-danger)' }}
                              aria-label={`#${b.id} foglalás lemondása`}
                              onClick={() => void cancelBooking(b)}>
                              Lemondás
                            </button>
                          )}
                        </td>
                      )}
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
