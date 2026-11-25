import { useEffect, useState, type FormEvent } from 'react'
import { AlertCircle, X } from 'lucide-react'
import client from '../api/client'
import DialogKeyboard from './DialogKeyboard'
import { formatTimeOnly, isWeekendDate, isoToLocalInput, localDateInputValue, localDateTimeToIso } from '../utils/dateTime'

export interface ReschedulableBooking {
  id: number | string
  teremId?: number
  teremNev?: string
  startTime?: string
  endTime?: string
}

interface RoomOption {
  id: number
  nev?: string
  aktivalva?: boolean
}

interface BusyRange {
  startTime: string
  endTime: string
}

interface Availability {
  foglalt: Array<BusyRange & { foglalasId?: number }>
  karbantartas: BusyRange[]
}

interface RescheduleBookingModalProps {
  booking: ReschedulableBooking
  // Teremváltást csak az adminisztrátor kérhet; az oktató csak az időpontot módosíthatja.
  allowRoomChange: boolean
  onClose: () => void
  onSaved: () => void
}

// A foglalás a 08:00-tól induló 45 perces sávokhoz igazodik.
const TIME_OPTIONS = Array.from({ length: 17 }, (_, index) => {
  const minutes = 8 * 60 + index * 45
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
})

function errorMessage(err: unknown, fallback: string): string {
  const axErr = err as { response?: { data?: { message?: string } } }
  return axErr.response?.data?.message || fallback
}

export default function RescheduleBookingModal({ booking, allowRoomChange, onClose, onSaved }: RescheduleBookingModalProps) {
  const initialStart = booking.startTime ? isoToLocalInput(booking.startTime) : `${localDateInputValue()}T08:00`
  const initialEnd = booking.endTime ? isoToLocalInput(booking.endTime) : `${localDateInputValue()}T08:45`
  const [date, setDate] = useState(initialStart.slice(0, 10))
  const [start, setStart] = useState(initialStart.slice(11, 16))
  const [end, setEnd] = useState(initialEnd.slice(11, 16))
  const [roomId, setRoomId] = useState(booking.teremId ? String(booking.teremId) : '')
  const [rooms, setRooms] = useState<RoomOption[]>([])
  const [availability, setAvailability] = useState<Availability | null>(null)
  const [error, setError] = useState('')
  const [isSaving, setIsSaving] = useState(false)

  useEffect(() => {
    if (!allowRoomChange) return
    let ignore = false
    client.get<RoomOption[]>('/rooms', { params: { aktivalva: true } })
      .then((response) => {
        if (!ignore && Array.isArray(response.data)) setRooms(response.data)
      })
      .catch(() => {
        if (!ignore) setError('A termek listáját nem sikerült betölteni.')
      })
    return () => {
      ignore = true
    }
  }, [allowRoomChange])

  // A kiválasztott nap foglaltsága (a saját foglalás nélkül), hogy az ütközés még mentés előtt kiderüljön.
  useEffect(() => {
    if (!roomId || !date || isWeekendDate(date)) {
      void Promise.resolve().then(() => setAvailability(null))
      return
    }
    let ignore = false
    client.get<Availability>(`/rooms/${roomId}/availability`, { params: { datum: date } })
      .then((response) => {
        if (!ignore) setAvailability(Array.isArray(response.data?.foglalt) ? response.data : null)
      })
      .catch(() => {
        if (!ignore) setAvailability(null)
      })
    return () => {
      ignore = true
    }
  }, [roomId, date])

  const conflict = (() => {
    if (!availability || !date || start >= end) return null
    const from = new Date(localDateTimeToIso(date, start)).getTime()
    const to = new Date(localDateTimeToIso(date, end)).getTime()
    const busy = [
      ...availability.foglalt.filter((item) => String(item.foglalasId) !== String(booking.id)),
      ...availability.karbantartas,
    ]
    return busy.find((item) => new Date(item.startTime).getTime() < to && new Date(item.endTime).getTime() > from) ?? null
  })()

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    if (isWeekendDate(date)) {
      setError('Hétvégére nem lehet foglalni.')
      return
    }
    if (start >= end) {
      setError('A befejezésnek a kezdés után kell lennie.')
      return
    }
    if (conflict) {
      setError(`Az idősáv ütközik: ${formatTimeOnly(conflict.startTime)}–${formatTimeOnly(conflict.endTime)}.`)
      return
    }

    setIsSaving(true)
    try {
      await client.put(`/bookings/${booking.id}`, {
        teremId: allowRoomChange && roomId ? Number(roomId) : undefined,
        startTime: localDateTimeToIso(date, start),
        endTime: localDateTimeToIso(date, end),
      })
      onSaved()
    } catch (err: unknown) {
      setError(errorMessage(err, 'A foglalás áthelyezése nem sikerült.'))
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={(event) => { if (event.target === event.currentTarget) onClose() }}>
      <DialogKeyboard onClose={onClose} />
      <div className="modal" style={{ maxWidth: 460 }} role="dialog" aria-modal="true" aria-labelledby="reschedule-title">
        <div className="modal-header">
          <h2 className="modal-title" id="reschedule-title">Foglalás áthelyezése</h2>
          <button type="button" className="modal-close" onClick={onClose} aria-label="Bezárás">
            <X size={18} strokeWidth={1.5} />
          </button>
        </div>

        {error && (
          <div className="alert alert-error" role="alert">
            <AlertCircle size={15} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={(event) => void handleSubmit(event)} noValidate>
          {allowRoomChange ? (
            <div className="form-group">
              <label className="form-label" htmlFor="reschedule-room">Terem</label>
              <select id="reschedule-room" className="form-select" value={roomId} onChange={(e) => setRoomId(e.target.value)}>
                {rooms.length === 0 && booking.teremId && <option value={booking.teremId}>{booking.teremNev}</option>}
                {rooms.map((room) => <option key={room.id} value={room.id}>{room.nev}</option>)}
              </select>
            </div>
          ) : (
            <p style={{ fontSize: 13, color: 'var(--color-gray-600)', marginBottom: 12 }}>Terem: <strong>{booking.teremNev}</strong></p>
          )}
          <div className="form-group">
            <label className="form-label" htmlFor="reschedule-date">Nap</label>
            <input id="reschedule-date" type="date" className="form-input" min={localDateInputValue()} value={date}
              onChange={(e) => setDate(e.target.value)} />
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label" htmlFor="reschedule-start">Kezdés</label>
              <select id="reschedule-start" className="form-select" value={start} onChange={(e) => setStart(e.target.value)}>
                {TIME_OPTIONS.slice(0, -1).map((time) => <option key={time} value={time}>{time}</option>)}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label" htmlFor="reschedule-end">Befejezés</label>
              <select id="reschedule-end" className="form-select" value={end} onChange={(e) => setEnd(e.target.value)}>
                {TIME_OPTIONS.slice(1).map((time) => <option key={time} value={time}>{time}</option>)}
              </select>
            </div>
          </div>
          {conflict && (
            <div className="alert alert-error" role="status">
              Ez az idősáv foglalt ({formatTimeOnly(conflict.startTime)}–{formatTimeOnly(conflict.endTime)}).
            </div>
          )}
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 16 }}>
            <button type="button" className="btn btn-secondary" onClick={onClose}>Mégse</button>
            <button type="submit" className="btn btn-primary" disabled={isSaving || Boolean(conflict)}>
              {isSaving ? 'Mentés…' : 'Áthelyezés mentése'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
