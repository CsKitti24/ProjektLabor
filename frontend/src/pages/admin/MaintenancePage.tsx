import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { Wrench, Plus, X, AlertCircle, Check, Trash2, RefreshCw } from 'lucide-react'
import client from '../../api/client'

interface RoomOption {
  id: number
  nev?: string
  name?: string
}

interface MaintenanceItem {
  id: number
  teremId: number
  teremNev: string
  felhasznaloNev: string
  startTime: string
  endTime: string
  leiras?: string
}

interface BookingConflict {
  foglalasId: number
  felhasznaloId: number
  felhasznaloNev: string
  teremId: number
  teremNev: string
  startTime: string
  endTime: string
  status: string
}

type ConflictAction = 'cancel' | 'move'

function toLocalDateTimeInput(value: string): string {
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}

function addMinutes(value: string, minutes: number): string {
  const date = new Date(value)
  date.setMinutes(date.getMinutes() + minutes)
  return toLocalDateTimeInput(date.toISOString())
}

function formatDateRange(start?: string, end?: string): string {
  if (!start) return '—'
  try {
    const dStart = new Date(start)
    const sDate = dStart.toLocaleDateString('hu-HU', { year: 'numeric', month: '2-digit', day: '2-digit' })
    const sTime = dStart.toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' })
    if (!end) return `${sDate} ${sTime}`
    const dEnd = new Date(end)
    const eTime = dEnd.toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' })
    return `${sDate} ${sTime} – ${eTime}`
  } catch {
    return `${start} – ${end || ''}`
  }
}

export default function MaintenancePage() {
  const [items, setItems] = useState<MaintenanceItem[]>([])
  const [rooms, setRooms] = useState<RoomOption[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [successMsg, setSuccessMsg] = useState('')

  const [showModal, setShowModal] = useState(false)
  const [teremId, setTeremId] = useState('')
  const [startDate, setStartDate] = useState(() => new Date().toISOString().split('T')[0])
  const [startTime, setStartTime] = useState('07:00')
  const [endTime, setEndTime] = useState('08:00')
  const [leiras, setLeiras] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [modalError, setModalError] = useState('')
  const [conflicts, setConflicts] = useState<BookingConflict[]>([])
  const [conflictActions, setConflictActions] = useState<Record<number, ConflictAction | ''>>({})
  const [moveStartTimes, setMoveStartTimes] = useState<Record<number, string>>({})
  const [moveRoomIds, setMoveRoomIds] = useState<Record<number, string>>({})

  const loadData = useCallback(async () => {
    setIsLoading(true)
    setError('')
    try {
      const [maintenanceResponse, roomsResponse] = await Promise.all([
        client.get<MaintenanceItem[]>('/maintenance'),
        client.get<RoomOption[]>('/rooms'),
      ])
      if (!Array.isArray(maintenanceResponse.data) || !Array.isArray(roomsResponse.data)) {
        throw new Error('A karbantartási vagy teremlista formátuma érvénytelen.')
      }
      setItems(maintenanceResponse.data)
      setRooms(roomsResponse.data)
      setTeremId(String(roomsResponse.data[0]?.id ?? ''))
    } catch {
      setItems([])
      setRooms([])
      setTeremId('')
      setError('A karbantartási vagy teremadatok betöltése nem sikerült.')
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    void Promise.resolve().then(() => loadData())
  }, [loadData])

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault()
    if (!teremId || !startDate || !startTime || !endTime) {
      setModalError('Kérjük töltsd ki az összes kötelező mezőt!')
      return
    }

    setModalError('')
    setIsSaving(true)

    try {
      await createMaintenance()
    } catch (err: unknown) {
      showCreateError(err)
    } finally {
      setIsSaving(false)
    }
  }

  const createMaintenance = async () => {
    const startIso = `${startDate}T${startTime}:00`
    const endIso = `${startDate}T${endTime}:00`
    await client.post('/maintenance', {
      teremId: Number(teremId),
      startTime: startIso,
      endTime: endIso,
      leiras: leiras.trim() || undefined,
    })
    setShowModal(false)
    setConflicts([])
    setConflictActions({})
    setLeiras('')
    setSuccessMsg('Karbantartási idősáv sikeresen ütemezve!')
    setTimeout(() => setSuccessMsg(''), 4000)
    loadData()
  }

  const showCreateError = (err: unknown) => {
    const axErr = err as { response?: { data?: { message?: string; conflicts?: BookingConflict[] } } }
    const payload = axErr.response?.data
    const newConflicts = payload?.conflicts ?? []
    setConflicts(newConflicts)
    setConflictActions(Object.fromEntries(newConflicts.map((item) => [item.foglalasId, ''])))
    setMoveStartTimes(Object.fromEntries(newConflicts.map((item) => {
      const maintenanceEnd = new Date(`${startDate}T${endTime}:00`)
      const bookingEnd = new Date(item.endTime)
      const proposed = maintenanceEnd > bookingEnd ? maintenanceEnd : bookingEnd
      return [item.foglalasId, toLocalDateTimeInput(proposed.toISOString())]
    })))
    setMoveRoomIds(Object.fromEntries(newConflicts.map((item) => [
      item.foglalasId,
      String(rooms.find((room) => room.id !== item.teremId)?.id ?? item.teremId),
    ])))
    setModalError(payload?.message || 'Nem sikerült rögzíteni a karbantartást.')
  }

  const resolveConflictsAndCreate = async () => {
    if (conflicts.some((item) => !conflictActions[item.foglalasId])) {
      setModalError('Minden ütköző foglalásnál válaszd ki a törlést vagy az áthelyezést.')
      return
    }
    if (conflicts.some((item) =>
      conflictActions[item.foglalasId] === 'move' &&
      (!moveStartTimes[item.foglalasId] || !moveRoomIds[item.foglalasId]))) {
      setModalError('Az áthelyezéshez új terem és kezdési időpont megadása kötelező.')
      return
    }

    setIsSaving(true)
    setModalError('')
    try {
      for (const booking of conflicts) {
        const action = conflictActions[booking.foglalasId]
        if (action === 'cancel') {
          await client.put(`/bookings/${booking.foglalasId}`, { status: 'Megszuntetve' })
          setConflicts((current) => current.filter((item) => item.foglalasId !== booking.foglalasId))
          continue
        }

        const start = moveStartTimes[booking.foglalasId]
        const startDateTime = new Date(start)
        const duration = (new Date(booking.endTime).getTime() - new Date(booking.startTime).getTime()) / 60000
        const end = addMinutes(startDateTime.toISOString(), duration)
        await client.put(`/bookings/${booking.foglalasId}`, {
          teremId: Number(moveRoomIds[booking.foglalasId]),
          startTime: `${start}:00`,
          endTime: `${end}:00`,
        })
        setConflicts((current) => current.filter((item) => item.foglalasId !== booking.foglalasId))
      }
      await createMaintenance()
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string; conflicts?: BookingConflict[] } } }
      if (axErr.response?.data?.conflicts) {
        showCreateError(err)
      } else {
        setModalError(`${axErr.response?.data?.message || 'A döntés feldolgozása sikertelen.'} A korábbi módosítások részben már mentődhettek; ellenőrizd a foglalásokat.`)
      }
    } finally {
      setIsSaving(false)
    }
  }

  const handleDelete = async (id: number) => {
    if (!window.confirm('Biztosan törölni szeretnéd ezt a karbantartást?')) return
    try {
      await client.delete(`/maintenance/${id}`)
      setItems((prev) => prev.filter((item) => item.id !== id))
    } catch {
      alert('Törlés sikertelen.')
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <Wrench size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Karbantartási idősávok</span>
          </h1>
          <p className="page-subtitle">Tervezett teremzárások és technikai munkálatok</p>
        </div>

        <button
          type="button"
          className="btn btn-primary"
          onClick={() => setShowModal(true)}
        >
          <Plus size={15} strokeWidth={1.5} />
          <span>Új karbantartás</span>
        </button>
      </div>

      {error && (
        <div className="alert alert-error" role="alert" style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
          <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{error}</span>
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => void loadData()}>
            <RefreshCw size={14} strokeWidth={1.5} />
            <span>Újrapróbálás</span>
          </button>
        </div>
      )}

      {successMsg && (
        <div className="alert alert-success">
          <Check size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{successMsg}</span>
        </div>
      )}

      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : error ? null : (
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Érintett terem</th>
                <th>Időtartam</th>
                <th>Karbantartó / Ütemező</th>
                <th>Leírás / Cél</th>
                <th style={{ textAlign: 'right' }}>Művelet</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Nincs aktív vagy tervezett karbantartási idősáv.
                  </td>
                </tr>
              ) : (
                items.map((item) => (
                  <tr key={item.id}>
                    <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                      {item.teremNev}
                    </td>
                    <td>{formatDateRange(item.startTime, item.endTime)}</td>
                    <td>{item.felhasznaloNev || 'Üzemeltetés'}</td>
                    <td style={{ color: 'var(--color-gray-600)' }}>{item.leiras || '—'}</td>
                    <td style={{ textAlign: 'right' }}>
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        style={{ color: 'var(--color-danger)' }}
                        onClick={() => handleDelete(item.id)}
                        title="Törlés"
                      >
                        <Trash2 size={14} strokeWidth={1.5} />
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Új karbantartás modál */}
      {showModal && (
        <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) setShowModal(false) }}>
          <div className="modal" style={{ maxWidth: 460 }}>
            <div className="modal-header">
              <div>
                <h2 className="modal-title">Karbantartás ütemezése</h2>
                <p style={{ fontSize: 12.5, color: 'var(--color-gray-500)', marginTop: 2 }}>
                  Terem zárolása karbantartási munkák idejére
                </p>
              </div>
              <button
                type="button"
                className="modal-close"
                onClick={() => setShowModal(false)}
                aria-label="Bezárás"
              >
                <X size={18} strokeWidth={1.5} />
              </button>
            </div>

            {modalError && (
              <div className="alert alert-error">
                <AlertCircle size={15} strokeWidth={1.5} style={{ flexShrink: 0 }} />
                <span>{modalError}</span>
              </div>
            )}

            <form onSubmit={handleCreate}>
              <div className="form-group">
                <label className="form-label">Érintett terem *</label>
                <select
                  className="form-select"
                  value={teremId}
                  onChange={(e) => setTeremId(e.target.value)}
                  required
                >
                  {rooms.map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.nev || r.name}
                    </option>
                  ))}
                </select>
              </div>

              {conflicts.length > 0 && (
                <section className="card" style={{ margin: '16px 0', padding: 16 }}>
                  <h3 style={{ marginBottom: 8 }}>Ütköző foglalások — döntés szükséges</h3>
                  <p style={{ marginBottom: 12 }}>
                    A karbantartás nem kerül mentésre, amíg az ütközéseket nem rendezed.
                    Foglalásonként választhatsz törlést vagy új időpontot/termet.
                  </p>
                  {conflicts.map((booking) => (
                    <div key={booking.foglalasId} style={{ borderTop: '1px solid var(--color-gray-200)', padding: '12px 0' }}>
                      <strong>#{booking.foglalasId} · {booking.felhasznaloNev} · {booking.teremNev}</strong>
                      <div>{formatDateRange(booking.startTime, booking.endTime)} · {booking.status}</div>
                      <div className="form-group" style={{ marginTop: 8 }}>
                        <label className="form-label" htmlFor={`conflict-${booking.foglalasId}`}>Teendő</label>
                        <select
                          id={`conflict-${booking.foglalasId}`}
                          className="form-select"
                          value={conflictActions[booking.foglalasId] ?? ''}
                          onChange={(event) => setConflictActions((current) => ({
                            ...current,
                            [booking.foglalasId]: event.target.value as ConflictAction | '',
                          }))}
                        >
                          <option value="">Válassz…</option>
                          <option value="cancel">Foglalás törlése</option>
                          <option value="move">Másik időpontba/terembe áthelyezés</option>
                        </select>
                      </div>
                      {conflictActions[booking.foglalasId] === 'move' && (
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                          <div className="form-group">
                            <label className="form-label" htmlFor={`move-room-${booking.foglalasId}`}>Új terem</label>
                            <select
                              id={`move-room-${booking.foglalasId}`}
                              className="form-select"
                              value={moveRoomIds[booking.foglalasId] ?? String(booking.teremId)}
                              onChange={(event) => setMoveRoomIds((current) => ({ ...current, [booking.foglalasId]: event.target.value }))}
                            >
                              {rooms.map((room) => <option key={room.id} value={room.id}>{room.nev}</option>)}
                            </select>
                          </div>
                          <div className="form-group">
                            <label className="form-label" htmlFor={`move-start-${booking.foglalasId}`}>Új kezdés</label>
                            <input
                              id={`move-start-${booking.foglalasId}`}
                              className="form-input"
                              type="datetime-local"
                              value={moveStartTimes[booking.foglalasId] ?? ''}
                              onChange={(event) => setMoveStartTimes((current) => ({ ...current, [booking.foglalasId]: event.target.value }))}
                              required
                            />
                          </div>
                        </div>
                      )}
                    </div>
                  ))}
                  <button type="button" className="btn btn-primary" onClick={resolveConflictsAndCreate} disabled={isSaving}>
                    {isSaving ? 'Foglalások kezelése…' : 'Döntések végrehajtása és karbantartás mentése'}
                  </button>
                </section>
              )}

              <div className="form-group">
                <label className="form-label">Dátum *</label>
                <input
                  type="date"
                  className="form-input"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  required
                />
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                <div className="form-group">
                  <label className="form-label">Kezdés *</label>
                  <input
                    type="time"
                    className="form-input"
                    value={startTime}
                    onChange={(e) => setStartTime(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Befejezés *</label>
                  <input
                    type="time"
                    className="form-input"
                    value={endTime}
                    onChange={(e) => setEndTime(e.target.value)}
                    required
                  />
                </div>
              </div>

              <div className="form-group">
                <label className="form-label">Karbantartás leírása</label>
                <textarea
                  className="form-textarea"
                  rows={3}
                  placeholder="pl. Projektor izzócsere, hálózati karbantartás..."
                  value={leiras}
                  onChange={(e) => setLeiras(e.target.value)}
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 24 }}>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowModal(false)}
                >
                  Mégse
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={isSaving}
                >
                  {isSaving ? 'Rögzítés…' : 'Karbantartás rögzítése'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
