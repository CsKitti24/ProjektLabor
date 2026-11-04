import { useState, useEffect, type FormEvent } from 'react'
import { Search, DoorOpen, Plus, AlertCircle, Check, X, Filter } from 'lucide-react'
import client from '../../api/client'
import { useAuth } from '../../auth/AuthContext'

interface Room {
  id: number | string
  nev?: string
  name?: string
  kapacitas?: number
  capacity?: number
  leiras?: string
  description?: string
  aktivalva?: boolean
  isActive?: boolean
  active?: boolean
}

export default function RoomsPage() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Adminisztrator'

  const [rooms, setRooms] = useState<Room[]>([])
  const [search, setSearch] = useState('')
  const [filterActiveOnly, setFilterActiveOnly] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [successMsg, setSuccessMsg] = useState('')

  // Új terem modál állapota (Admin funkció)
  const [showAddModal, setShowAddModal] = useState(false)
  const [newNev, setNewNev] = useState('')
  const [newKapacitas, setNewKapacitas] = useState(30)
  const [newLeiras, setNewLeiras] = useState('')
  const [newAktivalva, setNewAktivalva] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [modalError, setModalError] = useState('')

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

    setModalError('')
    setIsSubmitting(true)

    try {
      await client.post('/rooms', {
        nev: newNev.trim(),
        kapacitas: Number(newKapacitas),
        leiras: newLeiras.trim() || undefined,
        aktivalva: newAktivalva,
      })

      setShowAddModal(false)
      setNewNev('')
      setNewKapacitas(30)
      setNewLeiras('')
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
    if (!window.confirm(`Biztosan törölni szeretnéd a(z) "${name}" termet?`)) return

    try {
      await client.delete(`/rooms/${id}`)
      setSuccessMsg('Terem sikeresen törölve.')
      setTimeout(() => setSuccessMsg(''), 4000)
      setRooms((prev) => prev.filter((r) => r.id !== id))
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      alert(axErr.response?.data?.message || 'A terem törlése sikertelen.')
    }
  }

  const filtered = rooms.filter((r) => {
    const roomName = r.nev || r.name || ''
    const roomDesc = r.leiras || r.description || ''
    const isAct = r.aktivalva ?? r.isActive ?? r.active ?? true

    const matchesSearch =
      roomName.toLowerCase().includes(search.toLowerCase()) ||
      roomDesc.toLowerCase().includes(search.toLowerCase())

    if (filterActiveOnly && !isAct) return false
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
              onClick={() => setShowAddModal(true)}
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
      <div className="rooms-filter-bar">
        <div className="search-bar">
          <Search size={15} color="var(--color-gray-400)" strokeWidth={1.5} />
          <input
            type="text"
            placeholder="Keresés terem neve vagy leírás szerint…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
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
                <th style={{ width: '22%' }}>Terem neve</th>
                <th style={{ width: '15%' }}>Kapacitás</th>
                <th style={{ width: '40%' }}>Leírás / Megjegyzés</th>
                <th style={{ width: '13%' }}>Állapot</th>
                {isAdmin && <th style={{ width: '10%', textAlign: 'right' }}>Művelet</th>}
              </tr>
            </thead>
            <tbody>
              {filtered.length === 0 ? (
                <tr>
                  <td
                    colSpan={isAdmin ? 5 : 4}
                    style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}
                  >
                    Nincs a keresési feltételeknek megfelelő terem.
                  </td>
                </tr>
              ) : (
                filtered.map((room) => {
                  const roomName = room.nev || room.name || 'Névtelen terem'
                  const roomCap = room.kapacitas ?? room.capacity ?? '—'
                  const roomDesc = room.leiras ?? room.description ?? '—'
                  const isAct = room.aktivalva ?? room.isActive ?? room.active ?? true

                  return (
                    <tr key={room.id}>
                      <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                        {roomName}
                      </td>
                      <td>{roomCap} fő</td>
                      <td style={{ color: 'var(--color-gray-600)' }}>{roomDesc}</td>
                      <td>
                        {isAct ? (
                          <span className="badge badge-success">Aktív</span>
                        ) : (
                          <span className="badge badge-neutral">Inaktív</span>
                        )}
                      </td>
                      {isAdmin && (
                        <td style={{ textAlign: 'right' }}>
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            style={{ color: 'var(--color-danger)' }}
                            onClick={() => handleDeleteRoom(room.id, roomName)}
                            title="Terem törlése"
                          >
                            Törlés
                          </button>
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
          <div className="modal" style={{ maxWidth: 460 }}>
            <div className="modal-header">
              <div>
                <h2 className="modal-title">Új terem rögzítése</h2>
                <p style={{ fontSize: '12.5px', color: 'var(--color-gray-500)', marginTop: 2 }}>
                  Új oktatási vagy rendezvényhelyszín hozzáadása
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
                <label className="form-label">Terem neve / azonosítója *</label>
                <input
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
                <label className="form-label">Befogadóképesség (fő) *</label>
                <input
                  type="number"
                  className="form-input"
                  min={1}
                  max={1000}
                  value={newKapacitas}
                  onChange={(e) => setNewKapacitas(Number(e.target.value))}
                  required
                />
              </div>

              <div className="form-group">
                <label className="form-label">Leírás / Felszereltség</label>
                <textarea
                  className="form-input"
                  rows={3}
                  placeholder="pl. Projektor, hangosítás, 40 számítógép..."
                  value={newLeiras}
                  onChange={(e) => setNewLeiras(e.target.value)}
                />
              </div>

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
