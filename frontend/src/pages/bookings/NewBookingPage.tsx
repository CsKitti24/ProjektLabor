import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { PlusCircle, ArrowLeft, Check, AlertCircle, Calendar, Filter, Sparkles, RefreshCw } from 'lucide-react'
import client from '../../api/client'
import { fetchAllUsers, type UserListItem } from '../../api/users'
import { useAuth } from '../../auth/AuthContext'
import { localDateInputValue, localDateTimeToIso } from '../../utils/dateTime'

interface TimeRange {
  startTime: string
  endTime: string
}

interface RoomAvailability {
  szabad: TimeRange[]
  foglalt: TimeRange[]
  karbantartas: TimeRange[]
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' })
}

function isWeekend(date: string): boolean {
  const day = new Date(`${date}T12:00:00`).getDay()
  return day === 0 || day === 6
}

interface Room {
  id: number | string
  nev?: string
  name?: string
  kapacitas?: number
  leiras?: string
  felszereltseg?: string
  aktivalva?: boolean
}

const DEFAULT_SLOTS = [
  { start: '08:00', end: '09:30', label: '1. sáv (08:00 – 09:30)' },
  { start: '09:30', end: '11:00', label: '2. sáv (09:30 – 11:00)' },
  { start: '11:00', end: '12:30', label: '3. sáv (11:00 – 12:30)' },
  { start: '12:30', end: '14:00', label: '4. sáv (12:30 – 14:00)' },
  { start: '14:00', end: '15:30', label: '5. sáv (14:00 – 15:30)' },
  { start: '15:30', end: '17:00', label: '6. sáv (15:30 – 17:00)' },
  { start: '17:00', end: '18:30', label: '7. sáv (17:00 – 18:30)' },
  { start: '18:30', end: '20:00', label: '8. sáv (18:30 – 20:00)' },
]

const SLOT_SECONDS = 45 * 60

const COMMON_SPECIAL_NEEDS = [
  'Projektor',
  'Számítógépes labor',
  'Hangosítás / Mikrofon',
  'Interaktív tábla',
  'Előadói laptop',
]

export default function NewBookingPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const isAdmin = user?.role === 'Adminisztrator'
  // Adminisztrátor oktató nevében foglal: a foglalás csak oktató nevére szólhat.
  const [instructors, setInstructors] = useState<UserListItem[]>([])
  const [instructorId, setInstructorId] = useState('')
  const [availability, setAvailability] = useState<RoomAvailability | null>(null)
  const [rooms, setRooms] = useState<Room[]>([])
  const [roomId, setRoomId] = useState('')
  const [date, setDate] = useState(() => localDateInputValue())
  const [selectedSlotIndex, setSelectedSlotIndex] = useState(0)
  const [customStart, setCustomStart] = useState('08:00')
  const [customEnd, setCustomEnd] = useState('09:30')
  const [useCustomTime, setUseCustomTime] = useState(false)

  // Speciális igények állapota
  const [specialisIgenyek, setSpecialisIgenyek] = useState('')

  // Termek szűrése férőhely és felszereltség szerint
  const [minCapacityFilter, setMinCapacityFilter] = useState<string>('')
  const [equipmentFilter, setEquipmentFilter] = useState<string>('')

  const [error, setError] = useState('')
  const [roomsError, setRoomsError] = useState('')
  const [success, setSuccess] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const [isLoadingRooms, setIsLoadingRooms] = useState(true)

  const loadRooms = useCallback(async () => {
    setIsLoadingRooms(true)
    setRoomsError('')
    const params: Record<string, string | number | boolean> = { aktivalva: true }
    if (minCapacityFilter && Number(minCapacityFilter) > 0) {
      params.minKapacitas = Number(minCapacityFilter)
    }
    if (equipmentFilter.trim()) {
      params.felszereltseg = equipmentFilter.trim()
    }

    try {
      const response = await client.get<Room[]>('/rooms', { params })
      if (!Array.isArray(response.data)) {
        throw new Error('A teremlista formátuma érvénytelen.')
      }
      const activeRooms = response.data.filter((room) => room.aktivalva !== false)
      setRooms(activeRooms)
      setRoomId((currentRoomId) =>
        activeRooms.some((room) => String(room.id) === currentRoomId)
          ? currentRoomId
          : String(activeRooms[0]?.id ?? '')
      )
    } catch {
      setRooms([])
      setRoomId('')
      setRoomsError('A termek betöltése nem sikerült. Próbáld újra.')
    } finally {
      setIsLoadingRooms(false)
    }
  }, [equipmentFilter, minCapacityFilter])

  useEffect(() => {
    void Promise.resolve().then(() => loadRooms())
  }, [loadRooms])

  useEffect(() => {
    if (!isAdmin) return
    let ignore = false
    fetchAllUsers({ role: 'Oktato', aktiv: true })
      .then((list) => {
        if (ignore) return
        const sorted = [...list].sort((a, b) => (a.nev || a.username).localeCompare(b.nev || b.username, 'hu'))
        setInstructors(sorted)
      })
      .catch(() => {
        if (!ignore) setError('Az oktatók listájának betöltése nem sikerült.')
      })
    return () => {
      ignore = true
    }
  }, [isAdmin])

  // A kiválasztott terem foglaltsága az adott napon (foglalások és karbantartás), hogy ütköző sávot ne
  // kelljen próbálgatni.
  useEffect(() => {
    if (!roomId || !date || isWeekend(date)) {
      void Promise.resolve().then(() => setAvailability(null))
      return
    }
    let ignore = false
    client
      .get<RoomAvailability>(`/rooms/${roomId}/availability`, { params: { datum: date } })
      .then((response) => {
        if (!ignore) setAvailability(Array.isArray(response.data?.szabad) ? response.data : null)
      })
      .catch(() => {
        if (!ignore) setAvailability(null)
      })
    return () => {
      ignore = true
    }
  }, [roomId, date])

  const isSlotBusy = (start: string, end: string): boolean => {
    if (!availability) return false
    const slotStart = new Date(`${date}T${start}:00`).getTime()
    const slotEnd = new Date(`${date}T${end}:00`).getTime()
    return [...availability.foglalt, ...availability.karbantartas].some((busy) =>
      new Date(busy.startTime).getTime() < slotEnd && new Date(busy.endTime).getTime() > slotStart)
  }

  const toggleSpecialNeed = (need: string) => {
    const currentNeeds = specialisIgenyek
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean)

    if (currentNeeds.includes(need)) {
      const updated = currentNeeds.filter((n) => n !== need)
      setSpecialisIgenyek(updated.join(', '))
    } else {
      currentNeeds.push(need)
      setSpecialisIgenyek(currentNeeds.join(', '))
    }
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!roomId || !date) {
      setError('Kérjük, válassz termet és dátumot.')
      return
    }
    if (isWeekend(date)) {
      setError('Hétvégére nem lehet foglalni.')
      return
    }
    if (isAdmin && !instructorId) {
      setError('Válaszd ki, melyik oktató nevére szól a foglalás.')
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

    const startIso = localDateTimeToIso(date, start)
    const endIso = localDateTimeToIso(date, end)

    try {
      await client.post('/bookings', {
        teremId: Number(roomId),
        startTime: startIso,
        endTime: endIso,
        specialisIgenyek: specialisIgenyek.trim() || undefined,
        felhasznaloId: isAdmin ? Number(instructorId) : undefined,
      })

      setSuccess('A foglalás azonnal és sikeresen aktiválva a rendszerben!')
      setTimeout(() => navigate(isAdmin ? '/bookings' : '/my-bookings'), 1200)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A foglalás mentése sikertelen.')
    } finally {
      setIsLoading(false)
    }
  }

  const selectedRoomObj = rooms.find((r) => String(r.id) === roomId)

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <PlusCircle size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Új teremfoglalás</span>
          </h1>
          <p className="page-subtitle">
            {isAdmin ? 'Foglalás rögzítése egy oktató nevében' : 'Idősáv és terem lefoglalása oktatói szerepkörből'}
          </p>
        </div>

        <Link to={isAdmin ? '/bookings' : '/my-bookings'} className="btn btn-secondary btn-sm">
          <ArrowLeft size={14} strokeWidth={1.5} />
          <span>{isAdmin ? 'Vissza a foglalásokhoz' : 'Vissza a foglalásaimhoz'}</span>
        </Link>
      </div>

      <div className="card" style={{ maxWidth: 620 }}>
        {error && (
          <div className="alert alert-error">
            <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        {roomsError ? (
          <div className="alert alert-error" role="alert" style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
            <span>{roomsError}</span>
            <button type="button" className="btn btn-secondary btn-sm" onClick={() => void loadRooms()}>
              <RefreshCw size={14} strokeWidth={1.5} />
              <span>Újrapróbálás</span>
            </button>
          </div>
        ) : !isLoadingRooms && rooms.length === 0 ? (
          <div className="alert alert-info" role="status">A megadott feltételeknek megfelelő aktív terem nem található.</div>
        ) : null}

        {success && (
          <div className="alert alert-success">
            <Check size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{success}</span>
          </div>
        )}

        {/* Termek szűrése férőhely és felszereltség szerint */}
        <div style={{ background: 'var(--color-gray-50)', padding: 14, borderRadius: 8, marginBottom: 20, border: '1px solid var(--color-gray-200)' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, fontWeight: 600, color: 'var(--color-gray-700)', marginBottom: 10 }}>
            <Filter size={14} strokeWidth={1.5} />
            <span>Termek szűrése férőhelyszám és felszereltség alapján</span>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div>
              <label className="form-label" style={{ fontSize: 11.5 }}>Min. férőhely (fő)</label>
              <input
                type="number"
                min={1}
                placeholder="pl. 25"
                className="form-input"
                value={minCapacityFilter}
                onChange={(e) => setMinCapacityFilter(e.target.value)}
              />
            </div>
            <div>
              <label className="form-label" style={{ fontSize: 11.5 }}>Keresett felszereltség / típus</label>
              <input
                type="text"
                placeholder="pl. projektor, labor, pc..."
                className="form-input"
                value={equipmentFilter}
                onChange={(e) => setEquipmentFilter(e.target.value)}
              />
            </div>
          </div>
          {(minCapacityFilter || equipmentFilter) && (
            <div style={{ marginTop: 8, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontSize: 12, color: 'var(--color-gray-500)' }}>
                Találatok: <strong>{rooms.length} terem</strong>
              </span>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                style={{ fontSize: 11.5, padding: '2px 6px' }}
                onClick={() => {
                  setMinCapacityFilter('')
                  setEquipmentFilter('')
                }}
              >
                Szűrők törlése
              </button>
            </div>
          )}
        </div>

        <form onSubmit={handleSubmit}>
          {isAdmin && (
            <div className="form-group">
              <label className="form-label" htmlFor="booking-instructor">Oktató (akinek a nevére a foglalás szól) *</label>
              <select
                id="booking-instructor"
                className="form-select"
                value={instructorId}
                onChange={(e) => setInstructorId(e.target.value)}
                required
              >
                <option value="">Válassz oktatót…</option>
                {instructors.map((instructor) => (
                  <option key={instructor.id} value={instructor.id}>
                    {instructor.nev || instructor.username} ({instructor.username})
                  </option>
                ))}
              </select>
            </div>
          )}

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
              {rooms.length === 0 && <option value="">Nincs megfelelő aktív terem…</option>}
              {rooms.map((r) => {
                const equip = r.felszereltseg || r.leiras
                return (
                  <option key={r.id} value={r.id}>
                    {r.nev || r.name} {r.kapacitas ? `(${r.kapacitas} fős)` : ''} {equip ? `– ${equip}` : ''}
                  </option>
                )
              })}
            </select>
            {selectedRoomObj && (
              <div style={{ fontSize: 12, color: 'var(--color-gray-500)', marginTop: 4 }}>
                Férőhely: <strong>{selectedRoomObj.kapacitas} fő</strong>
                {(selectedRoomObj.felszereltseg || selectedRoomObj.leiras) && (
                  <span> | Felszereltség: <em>{selectedRoomObj.felszereltseg || selectedRoomObj.leiras}</em></span>
                )}
              </div>
            )}
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="booking-date">
              Dátum *
            </label>
            <input
              id="booking-date"
              type="date"
              className="form-input"
              min={localDateInputValue()}
              value={date}
              onChange={(e) => setDate(e.target.value)}
              required
            />
            {isWeekend(date) && (
              <div className="alert alert-error" role="alert" style={{ marginTop: 8 }}>Hétvégére nem lehet foglalni.</div>
            )}
            {availability && !isWeekend(date) && (
              <div style={{ fontSize: 12.5, color: 'var(--color-gray-600)', marginTop: 8 }} aria-label="Szabad idősávok">
                <strong>Szabad ezen a napon:</strong>{' '}
                {availability.szabad.length === 0
                  ? 'a terem egész nap foglalt.'
                  : availability.szabad.map((slot) => `${formatTime(slot.startTime)}–${formatTime(slot.endTime)}`).join(', ')}
              </div>
            )}
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
                  <option key={slot.label} value={index} disabled={isSlotBusy(slot.start, slot.end)}>
                    {slot.label}{isSlotBusy(slot.start, slot.end) ? ' – foglalt' : ''}
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
                    min="08:00"
                    max="19:15"
                    step={SLOT_SECONDS}
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
                    min="08:45"
                    max="20:00"
                    step={SLOT_SECONDS}
                    onChange={(e) => setCustomEnd(e.target.value)}
                    required
                  />
                </div>
              </div>
            )}
          </div>

          {/* Speciális igények kezelése */}
          <div className="form-group" style={{ marginTop: 16 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 6 }}>
              <Sparkles size={15} strokeWidth={1.5} color="var(--color-primary)" />
              <label className="form-label" style={{ marginBottom: 0 }}>
                Speciális igények (labor, projektor, egyéb technikai eszközök)
              </label>
            </div>

            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 8 }}>
              {COMMON_SPECIAL_NEEDS.map((need) => {
                const isSelected = specialisIgenyek
                  .split(',')
                  .map((s) => s.trim())
                  .includes(need)
                return (
                  <button
                    key={need}
                    type="button"
                    className={`btn btn-sm ${isSelected ? 'btn-primary' : 'btn-secondary'}`}
                    style={{ fontSize: 12, padding: '3px 8px' }}
                    onClick={() => toggleSpecialNeed(need)}
                  >
                    {isSelected ? '✓ ' : '+ '}
                    {need}
                  </button>
                )
              })}
            </div>

            <textarea
              className="form-input"
              rows={2}
              placeholder="pl. Projektor, laboratóriumi szoftverek, beépített hangosítás, egyéb technikai igény..."
              value={specialisIgenyek}
              onChange={(e) => setSpecialisIgenyek(e.target.value)}
              maxLength={500}
            />
            <div style={{ fontSize: 11, color: 'var(--color-gray-400)', textAlign: 'right', marginTop: 2 }}>
              {specialisIgenyek.length}/500 karakter
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 24 }}>
            <Link to="/my-bookings" className="btn btn-secondary">
              Mégse
            </Link>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isLoading || isLoadingRooms || roomsError !== '' || rooms.length === 0}
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
