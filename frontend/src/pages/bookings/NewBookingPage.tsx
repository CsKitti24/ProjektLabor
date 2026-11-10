import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { PlusCircle, ArrowLeft, Check, AlertCircle, Calendar, Filter, Sparkles, RefreshCw } from 'lucide-react'
import client from '../../api/client'

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
  { start: '09:45', end: '11:15', label: '2. sáv (09:45 – 11:15)' },
  { start: '11:30', end: '13:00', label: '3. sáv (11:30 – 13:00)' },
  { start: '13:15', end: '14:45', label: '4. sáv (13:15 – 14:45)' },
  { start: '15:00', end: '16:30', label: '5. sáv (15:00 – 16:30)' },
  { start: '16:45', end: '18:15', label: '6. sáv (16:45 – 18:15)' },
  { start: '18:30', end: '20:00', label: '7. sáv (18:30 – 20:00)' },
]

const COMMON_SPECIAL_NEEDS = [
  'Projektor',
  'Számítógépes labor',
  'Hangosítás / Mikrofon',
  'Interaktív tábla',
  'Előadói laptop',
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
        specialisIgenyek: specialisIgenyek.trim() || undefined,
      })

      setSuccess('A foglalás azonnal és sikeresen aktiválva a rendszerben!')
      setTimeout(() => navigate('/my-bookings'), 1200)
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
          <p className="page-subtitle">Idősáv és terem lefoglalása oktatói szerepkörből</p>
        </div>

        <Link to="/my-bookings" className="btn btn-secondary btn-sm">
          <ArrowLeft size={14} strokeWidth={1.5} />
          <span>Vissza a foglalásaimhoz</span>
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
