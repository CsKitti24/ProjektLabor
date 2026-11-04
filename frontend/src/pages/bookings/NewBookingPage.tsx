import { useState, useEffect, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { PlusCircle, ArrowLeft, Check, AlertCircle, Calendar } from 'lucide-react'
import client from '../../api/client'

interface Room {
  id: number | string
  nev?: string
  name?: string
  kapacitas?: number
  aktivalva?: boolean
}

const DEFAULT_SLOTS = [
  { start: '08:00', end: '09:30', label: '1. sáv (08:00 – 09:30)' },
  { start: '09:45', end: '11:15', label: '2. sáv (09:45 – 11:15)' },
  { start: '11:30', end: '13:00', label: '3. sáv (11:30 – 13:00)' },
  { start: '13:15', end: '14:45', label: '4. sáv (13:15 – 14:45)' },
  { start: '15:00', end: '16:30', label: '5. sáv (15:00 – 16:30)' },
  { start: '16:45', end: '18:15', label: '6. sáv (16:45 – 18:15)' },
  { start: '18:30', end: '20:00', label: '7. sáv (18:30 – 20:00)' },
]

export default function NewBookingPage() {
  const navigate = useNavigate()
  const [rooms, setRooms] = useState<Room[]>([])
  const [roomId, setRoomId] = useState('')
  const [date, setDate] = useState(() => {
    const today = new Date()
    return today.toISOString().split('T')[0]
  })
  const [selectedSlotIndex, setSelectedSlotIndex] = useState(0)
  const [customStart, setCustomStart] = useState('08:00')
  const [customEnd, setCustomEnd] = useState('09:30')
  const [useCustomTime, setUseCustomTime] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [isLoading, setIsLoading] = useState(false)

  useEffect(() => {
    client
      .get<Room[]>('/rooms')
      .then((r) => {
        const activeRooms = r.data.filter((rm) => rm.aktivalva !== false)
        setRooms(activeRooms)
        if (activeRooms.length > 0) {
          setRoomId(String(activeRooms[0].id))
        }
      })
      .catch(() => {})
  }, [])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!roomId || !date) {
      setError('Kérjük, válassz termet és dátumot.')
      return
    }

    const start = useCustomTime ? customStart : DEFAULT_SLOTS[selectedSlotIndex].start
    const end = useCustomTime ? customEnd : DEFAULT_SLOTS[selectedSlotIndex].end

    if (start >= end) {
      setError('A befejezési időpontnak a kezdési időpont után kell lennie.')
      return
    }

    setError('')
    setIsLoading(true)

    const startIso = `${date}T${start}:00`
    const endIso = `${date}T${end}:00`

    try {
      await client.post('/bookings', {
        teremId: Number(roomId),
        startTime: startIso,
        endTime: endIso,
      })

      setSuccess('A foglalás sikeresen rögzítve a rendszerben!')
      setTimeout(() => navigate('/my-bookings'), 1200)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A foglalás mentése sikertelen.')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <PlusCircle size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Új teremfoglalás</span>
          </h1>
          <p className="page-subtitle">Idősáv és terem lefoglalása oktatási célra</p>
        </div>

        <Link to="/my-bookings" className="btn btn-secondary btn-sm">
          <ArrowLeft size={14} strokeWidth={1.5} />
          <span>Vissza a foglalásaimhoz</span>
        </Link>
      </div>

      <div className="card" style={{ maxWidth: 540 }}>
        {error && (
          <div className="alert alert-error">
            <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        {success && (
          <div className="alert alert-success">
            <Check size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{success}</span>
          </div>
        )}

        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label className="form-label" htmlFor="booking-room">
              Terem kiválasztása *
            </label>
            <select
              id="booking-room"
              className="form-select"
              value={roomId}
              onChange={(e) => setRoomId(e.target.value)}
              required
            >
              {rooms.length === 0 && <option value="">Termek betöltése…</option>}
              {rooms.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.nev || r.name} {r.kapacitas ? `(${r.kapacitas} fő)` : ''}
                </option>
              ))}
            </select>
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="booking-date">
              Dátum *
            </label>
            <input
              id="booking-date"
              type="date"
              className="form-input"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              required
            />
          </div>

          <div className="form-group">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <label className="form-label">Idősáv megadása *</label>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                style={{ padding: '2px 6px', fontSize: 12, color: 'var(--color-primary)' }}
                onClick={() => setUseCustomTime(!useCustomTime)}
              >
                {useCustomTime ? 'Váltás fix sávokra' : 'Egyéni időpont beállítása'}
              </button>
            </div>

            {!useCustomTime ? (
              <select
                className="form-select"
                value={selectedSlotIndex}
                onChange={(e) => setSelectedSlotIndex(Number(e.target.value))}
              >
                {DEFAULT_SLOTS.map((slot, index) => (
                  <option key={slot.label} value={index}>
                    {slot.label}
                  </option>
                ))}
              </select>
            ) : (
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                <div>
                  <label className="form-label" style={{ fontSize: 11.5, color: 'var(--color-gray-500)' }}>
                    Kezdés
                  </label>
                  <input
                    type="time"
                    className="form-input"
                    value={customStart}
                    onChange={(e) => setCustomStart(e.target.value)}
                    required
                  />
                </div>
                <div>
                  <label className="form-label" style={{ fontSize: 11.5, color: 'var(--color-gray-500)' }}>
                    Befejezés
                  </label>
                  <input
                    type="time"
                    className="form-input"
                    value={customEnd}
                    onChange={(e) => setCustomEnd(e.target.value)}
                    required
                  />
                </div>
              </div>
            )}
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 28 }}>
            <Link to="/my-bookings" className="btn btn-secondary">
              Mégse
            </Link>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isLoading || rooms.length === 0}
            >
              {isLoading ? (
                <>
                  <span className="spinner" />
                  <span>Rögzítés…</span>
                </>
              ) : (
                <>
                  <Calendar size={15} strokeWidth={1.5} />
                  <span>Foglalás véglegesítése</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
