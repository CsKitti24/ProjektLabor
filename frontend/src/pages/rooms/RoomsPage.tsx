import { useState, useEffect, type FormEvent } from 'react'
import { Search, DoorOpen, Plus, AlertCircle, Check, X, Filter, Pencil, RotateCcw } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import client from '../../api/client'
import DialogKeyboard from '../../components/DialogKeyboard'
import { useAuth } from '../../auth/AuthContext'
import { useConfirm } from '../../components/confirmContext'

interface Room {
  id: number | string
  nev?: string
  name?: string
  kapacitas?: number
  capacity?: number
  leiras?: string
  description?: string
  felszereltseg?: string
  aktivalva?: boolean
  isActive?: boolean
  active?: boolean
}

export default function RoomsPage() {
  const confirm = useConfirm()
  const { user } = useAuth()
  const isAdmin = user?.role === 'Adminisztrator'

  const [rooms, setRooms] = useState<Room[]>([])
  // A navigációs sáv keresője ?q= paraméterrel ide irányít.
  const [searchParams] = useSearchParams()
  const [search, setSearch] = useState(() => searchParams.get('q') ?? '')
  const [minKapacitasFilter, setMinKapacitasFilter] = useState('')
  const [filterActiveOnly, setFilterActiveOnly] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [successMsg, setSuccessMsg] = useState('')

  // Új terem modál állapota (Admin funkció)
  const [showAddModal, setShowAddModal] = useState(false)
  const [newNev, setNewNev] = useState('')
  const [newKapacitas, setNewKapacitas] = useState(30)
  const [newLeiras, setNewLeiras] = useState('')
  const [newFelszereltseg, setNewFelszereltseg] = useState('')
  const [newAktivalva, setNewAktivalva] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [modalError, setModalError] = useState('')
  // Szerkesztéskor a módosított terem; null esetén a modál új termet rögzít.
  const [editingRoom, setEditingRoom] = useState<Room | null>(null)

  const showSuccess = (message: string) => {
    setSuccessMsg(message)
    setTimeout(() => setSuccessMsg(''), 4000)
  }

  const openCreateModal = () => {
    setEditingRoom(null)
    setNewNev('')
    setNewKapacitas(30)
    setNewLeiras('')
    setNewFelszereltseg('')
    setNewAktivalva(true)
    setModalError('')
    setShowAddModal(true)
  }

  const openEditModal = (room: Room) => {
    setEditingRoom(room)
    setNewNev(room.nev || room.name || '')
    setNewKapacitas(room.kapacitas ?? room.capacity ?? 1)
    setNewLeiras(room.leiras ?? room.description ?? '')
    setNewFelszereltseg(room.felszereltseg ?? '')
    setModalError('')
    setShowAddModal(true)
  }

  const handleReactivateRoom = async (room: Room) => {
    try {
      await client.put(`/rooms/${room.id}`, { aktivalva: true })
      setRooms((prev) => prev.map((r) => (r.id === room.id ? { ...r, aktivalva: true } : r)))
      showSuccess(`A(z) ${room.nev || room.name} terem újra aktív és foglalható.`)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A terem újraaktiválása sikertelen.')
    }
  }

  const fetchRooms = () => {
    setIsLoading(true)
    client
      .get<Room[]>('/rooms')
      .then((res) => {
        setRooms(res.data)
        setError('')
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a termeket.')
      })
      .finally(() => {
        setIsLoading(false)
      })
  }

  useEffect(() => {
    let ignore = false
    client
      .get<Room[]>('/rooms')
      .then((res) => {
        if (!ignore) {
          setRooms(res.data)
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const axErr = err as { response?: { data?: { message?: string } } }
          setError(axErr.response?.data?.message || 'Nem sikerült betölteni a termeket.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setIsLoading(false)
        }
      })
    return () => {
      ignore = true
    }
  }, [])

  const handleCreateRoom = async (e: FormEvent) => {
    e.preventDefault()
    if (!newNev.trim()) {
      setModalError('A terem nevének megadása kötelező.')
      return
    }
    const capacity = Number(newKapacitas)
    if (!Number.isInteger(capacity) || capacity < 1 || capacity > 10000) {
      setModalError('A terem kapacitása 1 és 10 000 között lehet.')
      return
    }

    setModalError('')
    setIsSubmitting(true)

    try {
      if (editingRoom) {
        await client.put(`/rooms/${editingRoom.id}`, {
          nev: newNev.trim(),
          kapacitas: Number(newKapacitas),
          leiras: newLeiras.trim(),
          felszereltseg: newFelszereltseg.trim(),
        })
        setShowAddModal(false)
        setEditingRoom(null)
        showSuccess('A terem adatai frissítve.')
        fetchRooms()
        return
      }

      await client.post('/rooms', {
        nev: newNev.trim(),
        kapacitas: Number(newKapacitas),
        leiras: newLeiras.trim() || undefined,
        felszereltseg: newFelszereltseg.trim() || undefined,
        aktivalva: newAktivalva,
      })

      setShowAddModal(false)
      setNewNev('')
      setNewKapacitas(30)
      setNewLeiras('')
      setNewFelszereltseg('')
      setNewAktivalva(true)
      setSuccessMsg('A terem sikeresen létrehozva.')
      setTimeout(() => setSuccessMsg(''), 4000)
      fetchRooms()
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setModalError(axErr.response?.data?.message || 'Hiba történt a terem mentésekor.')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleDeleteRoom = async (id: number | string, name: string) => {
    if (!(await confirm({ title: 'Terem deaktiválása', message: `Biztosan deaktiválod a(z) "${name}" termet? Az előzményei megmaradnak, és később újraaktiválható.`, confirmLabel: 'Deaktiválás', danger: true }))) return

    // A törlés valójában deaktiválás: a terem a listában marad "Inaktív" jelöléssel.
    const removeRoom = (message: string) => {
      showSuccess(message)
      setRooms((prev) => prev.map((r) => (r.id === id ? { ...r, aktivalva: false } : r)))
    }

    try {
      await client.delete(`/rooms/${id}`)
      removeRoom('A terem deaktiválva.')
    } catch (err: unknown) {
      const axErr = err as { response?: { status?: number; data?: { message?: string; jovobeliFoglalasok?: number } } }
      const futureCount = axErr.response?.data?.jovobeliFoglalasok
      if (axErr.response?.status === 409 && futureCount) {
        // A teremnek jövőbeli foglalásai vannak: csak kifejezett megerősítésre mondjuk le őket.
        if (!(await confirm({ title: 'Jövőbeli foglalások', message: `${axErr.response?.data?.message}\n\nLemondod mind a(z) ${futureCount} foglalást (az oktatók értesítést kapnak), és deaktiválod a termet?`, confirmLabel: 'Lemondás és deaktiválás', danger: true }))) return
        try {
          await client.delete(`/rooms/${id}`, { params: { foglalasokLemondasa: true } })
          removeRoom(`A terem deaktiválva, ${futureCount} jövőbeli foglalás lemondva.`)
        } catch (retryErr: unknown) {
          const retryAxErr = retryErr as { response?: { data?: { message?: string } } }
          setError(retryAxErr.response?.data?.message || 'A terem deaktiválása sikertelen.')
        }
        return
      }
      setError(axErr.response?.data?.message || 'A terem deaktiválása sikertelen.')
    }
  }

  const filtered = rooms.filter((r) => {
    const roomName = r.nev || r.name || ''
    const roomDesc = r.leiras || r.description || ''
    const roomEquip = r.felszereltseg || ''
    const roomCap = r.kapacitas ?? r.capacity ?? 0
    const isAct = r.aktivalva ?? r.isActive ?? r.active ?? true

    const matchesSearch =
      roomName.toLowerCase().includes(search.toLowerCase()) ||
      roomDesc.toLowerCase().includes(search.toLowerCase()) ||
      roomEquip.toLowerCase().includes(search.toLowerCase())

    if (filterActiveOnly && !isAct) return false
    if (minKapacitasFilter && roomCap < Number(minKapacitasFilter)) return false

    return matchesSearch
  })

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <DoorOpen size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Termek nyilvántartása</span>
          </h1>
          <p className="page-subtitle">Intézményi oktatási és rendezvénytermek állapota</p>
        </div>

        <div className="rooms-header-actions">
          {isAdmin && (
            <button
              type="button"
              className="btn btn-primary"
              onClick={openCreateModal}
            >
              <Plus size={15} strokeWidth={1.5} />
              <span>Új terem</span>
            </button>
          )}
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{error}</span>
        </div>
      )}

      {successMsg && (
        <div className="alert alert-success">
          <Check size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{successMsg}</span>
        </div>
      )}

      {/* Szűrősáv */}
      <div className="rooms-filter-bar" style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
        <div className="search-bar" style={{ flex: 1, minWidth: 220 }}>
          <Search size={15} color="var(--color-gray-400)" strokeWidth={1.5} />
          <input
            type="text"
            placeholder="Keresés név, leírás vagy felszereltség szerint…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ fontSize: 13, color: 'var(--color-gray-600)', whiteSpace: 'nowrap' }}>Min. férőhely:</span>
          <input
            type="number"
            min={1}
            placeholder="pl. 20"
            className="form-input"
            style={{ width: 85, padding: '6px 10px', height: 36 }}
            value={minKapacitasFilter}
            onChange={(e) => setMinKapacitasFilter(e.target.value)}
          />
        </div>

        <button
          type="button"
          className={`btn btn-sm ${filterActiveOnly ? 'btn-primary' : 'btn-secondary'}`}
          onClick={() => setFilterActiveOnly(!filterActiveOnly)}
        >
          <Filter size={13} strokeWidth={1.5} />
          <span>{filterActiveOnly ? 'Csak aktív (aktív szűrés)' : 'Összes terem'}</span>
        </button>
      </div>

      {/* Terem lista táblázat */}
      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : (
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th style={{ width: '20%' }}>Terem neve</th>
                <th style={{ width: '12%' }}>Kapacitás</th>
                <th style={{ width: '25%' }}>Felszereltség</th>
                <th style={{ width: '28%' }}>Leírás / Megjegyzés</th>
                <th style={{ width: '10%' }}>Állapot</th>
                {isAdmin && <th style={{ width: '15%', textAlign: 'right' }}>Művelet</th>}
              </tr>
            </thead>
            <tbody>
              {filtered.length === 0 ? (
                <tr>
                  <td
                    colSpan={isAdmin ? 6 : 5}
                    style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}
                  >
                    Nincs a keresési feltételeknek megfelelő terem.
                  </td>
                </tr>
              ) : (
                filtered.map((room) => {
                  const roomName = room.nev || room.name || 'Névtelen terem'
                  const roomCap = room.kapacitas ?? room.capacity ?? '—'
                  const roomEquip = room.felszereltseg ?? '—'
                  const roomDesc = room.leiras ?? room.description ?? '—'
                  const isAct = room.aktivalva ?? room.isActive ?? room.active ?? true

                  return (
                    <tr key={room.id}>
                      <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                        {roomName}
                      </td>
                      <td>{roomCap} fő</td>
                      <td>
                        {roomEquip !== '—' ? (
                          <span style={{ background: 'var(--color-gray-100)', padding: '2px 8px', borderRadius: 4, fontSize: 13 }}>
                            {roomEquip}
                          </span>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td style={{ color: 'var(--color-gray-600)' }}>{roomDesc}</td>
                      <td>
                        {isAct ? (
                          <span className="badge badge-success">Aktív</span>
                        ) : (
                          <span className="badge badge-neutral">Inaktív</span>
                        )}
                      </td>
                      {isAdmin && (
                        <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            onClick={() => openEditModal(room)}
                            aria-label={`${roomName} szerkesztése`}
                            title="Szerkesztés"
                          >
                            <Pencil size={13} strokeWidth={1.5} />
                            <span>Szerkesztés</span>
                          </button>
                          {isAct ? (
                            <button
                              type="button"
                              className="btn btn-ghost btn-sm"
                              style={{ color: 'var(--color-danger)' }}
                              onClick={() => handleDeleteRoom(room.id, roomName)}
                              title="Terem deaktiválása"
                            >
                              Deaktiválás
                            </button>
                          ) : (
                            <button
                              type="button"
                              className="btn btn-ghost btn-sm"
                              onClick={() => void handleReactivateRoom(room)}
                              title="Terem újraaktiválása"
                            >
                              <RotateCcw size={13} strokeWidth={1.5} />
                              <span>Újraaktiválás</span>
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

      {/* Új terem modál (Adminisztrátornak) */}
      {showAddModal && (
        <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) setShowAddModal(false) }}>
          <DialogKeyboard onClose={() => setShowAddModal(false)} />
          <div className="modal" style={{ maxWidth: 460 }} role="dialog" aria-modal="true">
            <div className="modal-header">
              <div>
                <h2 className="modal-title">{editingRoom ? 'Terem szerkesztése' : 'Új terem rögzítése'}</h2>
                <p style={{ fontSize: '12.5px', color: 'var(--color-gray-500)', marginTop: 2 }}>
                  {editingRoom ? 'A terem adatainak módosítása' : 'Új oktatási vagy rendezvényhelyszín hozzáadása'}
                </p>
              </div>
              <button
                type="button"
                className="modal-close"
                onClick={() => setShowAddModal(false)}
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

            <form onSubmit={handleCreateRoom}>
              <div className="form-group">
                <label className="form-label" htmlFor="room-name">Terem neve / azonosítója *</label>
                <input
                  id="room-name"
                  type="text"
                  className="form-input"
                  placeholder="pl. I. előadó (IB028) vagy 204-es terem"
                  value={newNev}
                  onChange={(e) => setNewNev(e.target.value)}
                  autoFocus
                  required
                />
              </div>

              <div className="form-group">
                <label className="form-label" htmlFor="room-capacity">Befogadóképesség (fő) *</label>
                <input
                  id="room-capacity"
                  type="number"
                  className="form-input"
                  min={1}
                  max={10000}
                  value={newKapacitas}
                  onChange={(e) => setNewKapacitas(Number(e.target.value))}
                  required
                />
              </div>

              <div className="form-group">
                <label className="form-label" htmlFor="room-equipment">Felszereltség</label>
                <input
                  id="room-equipment"
                  type="text"
                  className="form-input"
                  placeholder="pl. Projektor, 30 PC, Labor, Hangosítás..."
                  value={newFelszereltseg}
                  onChange={(e) => setNewFelszereltseg(e.target.value)}
                />
              </div>

              <div className="form-group">
                <label className="form-label" htmlFor="room-description">Leírás / Megjegyzés</label>
                <textarea
                  id="room-description"
                  className="form-input"
                  rows={3}
                  placeholder="pl. Északi szárny 2. emelet..."
                  value={newLeiras}
                  onChange={(e) => setNewLeiras(e.target.value)}
                />
              </div>

              {!editingRoom && (
              <div className="form-group" style={{ flexDirection: 'row', alignItems: 'center', gap: 10, marginTop: 4 }}>
                <input
                  type="checkbox"
                  id="chk-aktivalva"
                  checked={newAktivalva}
                  onChange={(e) => setNewAktivalva(e.target.checked)}
                  style={{ width: 16, height: 16, accentColor: 'var(--color-primary)' }}
                />
                <label htmlFor="chk-aktivalva" className="form-label" style={{ marginBottom: 0, cursor: 'pointer' }}>
                  A terem azonnal aktív és foglalható
                </label>
              </div>
              )}

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 24 }}>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowAddModal(false)}
                >
                  Mégse
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={isSubmitting}
                >
                  {isSubmitting ? 'Mentés…' : 'Terem mentése'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        .rooms-header-actions {
          display: flex;
          align-items: center;
          gap: 12px;
        }

        .rooms-filter-bar {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 16px;
          margin-bottom: 18px;
          flex-wrap: wrap;
        }

        .rooms-filter-bar .search-bar {
          flex: 1;
          max-width: 380px;
        }

        .rooms-filter-bar .search-bar input {
          width: 100%;
        }
      `}</style>
    </div>
  )
}
