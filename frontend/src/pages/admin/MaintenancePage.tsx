import { useState, useEffect, type FormEvent } from 'react'
import { Wrench, Plus, X, AlertCircle, Check, Trash2 } from 'lucide-react'
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

  const loadData = () => {
    setIsLoading(true)
    Promise.all([
      client.get<MaintenanceItem[]>('/maintenance'),
      client.get<RoomOption[]>('/rooms').catch(() => ({ data: [] })),
    ])
      .then(([mRes, rRes]) => {
        setItems(mRes.data)
        setRooms(rRes.data)
        if (rRes.data.length > 0) setTeremId(String(rRes.data[0].id))
        setError('')
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a karbantartásokat.')
      })
      .finally(() => setIsLoading(false))
  }

  useEffect(() => {
    loadData()
  }, [])

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault()
    if (!teremId || !startDate || !startTime || !endTime) {
      setModalError('Kérjük töltsd ki az összes kötelező mezőt!')
      return
    }

    setModalError('')
    setIsSaving(true)

    const startIso = `${startDate}T${startTime}:00`
    const endIso = `${startDate}T${endTime}:00`

    try {
      await client.post('/maintenance', {
        teremId: Number(teremId),
        startTime: startIso,
        endTime: endIso,
        leiras: leiras.trim() || undefined,
      })

      setShowModal(false)
      setLeiras('')
      setSuccessMsg('Karbantartási idősáv sikeresen ütemezve!')
      setTimeout(() => setSuccessMsg(''), 4000)
      loadData()
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setModalError(axErr.response?.data?.message || 'Nem sikerült rögzíteni a karbantartást.')
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

      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : (
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
