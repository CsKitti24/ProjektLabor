import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { FileText, Plus, X, AlertCircle, Check } from 'lucide-react'
import client from '../../api/client'

interface RoomOption {
  id: number
  nev?: string
  name?: string
}

interface Ticket {
  id: number | string
  teremId?: number
  teremNev?: string
  kategoria?: string
  leiras?: string
  statusz?: string
  status?: string
  felhasznaloNev?: string
  reporterName?: string
  createdAt?: string
}

function formatDate(isoString?: string): string {
  if (!isoString) return '—'
  try {
    const d = new Date(isoString)
    return d.toLocaleDateString('hu-HU', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return isoString
  }
}

function getStatusBadge(status: string | undefined) {
  switch (status) {
    case 'Uj':
    case 'Open':
    case 'Nyitott':
      return <span className="badge badge-warning">Új</span>
    case 'Folyamatban':
    case 'InProgress':
      return <span className="badge badge-info">Folyamatban</span>
    case 'Megoldva':
    case 'Resolved':
      return <span className="badge badge-success">Megoldva</span>
    case 'Lezarva':
    case 'Closed':
    case 'Lezárt':
      return <span className="badge badge-neutral">Lezárva</span>
    default:
      return <span className="badge badge-neutral">{status ?? '—'}</span>
  }
}

export default function TicketsPage() {
  const [tickets, setTickets] = useState<Ticket[]>([])
  const [rooms, setRooms] = useState<RoomOption[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [successMsg, setSuccessMsg] = useState('')

  const [showModal, setShowModal] = useState(false)
  const [selectedRoomId, setSelectedRoomId] = useState('')
  const [kategoria, setKategoria] = useState('Technikai hiba')
  const [leiras, setLeiras] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [modalError, setModalError] = useState('')

  const loadTickets = useCallback(() => {
    setIsLoading(true)
    client
      .get<{ items?: Ticket[] } | Ticket[]>('/tickets')
      .then((r) => {
        const list = Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
        setTickets(list)
        setError('')
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a hibajegyeket.')
      })
      .finally(() => setIsLoading(false))
  }, [])

  useEffect(() => {
    loadTickets()
    client
      .get<RoomOption[]>('/rooms')
      .then((res) => {
        setRooms(res.data)
        if (res.data.length > 0) setSelectedRoomId(String(res.data[0].id))
      })
      .catch(() => {})
  }, [loadTickets])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!selectedRoomId || !leiras.trim()) {
      setModalError('Kérjük, válassz termet és írd le a hibát!')
      return
    }

    setModalError('')
    setIsSaving(true)

    try {
      await client.post('/tickets', {
        teremId: Number(selectedRoomId),
        kategoria,
        leiras: leiras.trim(),
      })

      setShowModal(false)
      setLeiras('')
      setSuccessMsg('A hibajegy sikeresen rögzítve az üzemeltetés felé!')
      setTimeout(() => setSuccessMsg(''), 4000)
      loadTickets()
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setModalError(axErr.response?.data?.message || 'A hibajegy létrehozása sikertelen.')
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <FileText size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Hibajegyek és karbantartási igények</span>
          </h1>
          <p className="page-subtitle">Termekkel és eszközökkel kapcsolatos hibabejelentések</p>
        </div>

        <button
          type="button"
          className="btn btn-primary"
          onClick={() => setShowModal(true)}
        >
          <Plus size={15} strokeWidth={1.5} />
          <span>Új hibajegy</span>
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
                <th>Azonosító</th>
                <th>Érintett terem</th>
                <th>Kategória</th>
                <th>Leírás</th>
                <th>Bejelentő</th>
                <th>Dátum</th>
                <th>Státusz</th>
              </tr>
            </thead>
            <tbody>
              {tickets.length === 0 ? (
                <tr>
                  <td colSpan={7} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Nincsenek aktív hibajegyek rögzítve.
                  </td>
                </tr>
              ) : (
                tickets.map((t) => (
                  <tr key={t.id}>
                    <td style={{ fontWeight: 600 }}>#{t.id}</td>
                    <td>{t.teremNev || `Terem #${t.teremId}`}</td>
                    <td>
                      <span className="badge badge-neutral">{t.kategoria || 'Általános'}</span>
                    </td>
                    <td style={{ color: 'var(--color-gray-700)', maxWidth: 300 }}>{t.leiras || '—'}</td>
                    <td>{t.felhasznaloNev || t.reporterName || '—'}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>{formatDate(t.createdAt)}</td>
                    <td>{getStatusBadge(t.statusz || t.status)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Új hibajegy felugró ablak */}
      {showModal && (
        <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) setShowModal(false) }}>
          <div className="modal" style={{ maxWidth: 460 }}>
            <div className="modal-header">
              <div>
                <h2 className="modal-title">Új hibajegy beküldése</h2>
                <p style={{ fontSize: 12.5, color: 'var(--color-gray-500)', marginTop: 2 }}>
                  Tantermi hiba vagy hiányosság jelzése az üzemeltetésnek
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

            <form onSubmit={handleSubmit}>
              <div className="form-group">
                <label className="form-label">Érintett terem *</label>
                <select
                  className="form-select"
                  value={selectedRoomId}
                  onChange={(e) => setSelectedRoomId(e.target.value)}
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
                <label className="form-label">Hiba kategóriája *</label>
                <select
                  className="form-select"
                  value={kategoria}
                  onChange={(e) => setKategoria(e.target.value)}
                >
                  <option value="Technikai hiba">Technikai hiba (projektor, PC, hang)</option>
                  <option value="Bútorzat / Felszerelés">Bútorzat / Felszerelés</option>
                  <option value="Zár / Kulcs probléma">Zár / Kulcs probléma</option>
                  <option value="Takarítási igény">Takarítási igény</option>
                  <option value="Egyéb észrevétel">Egyéb észrevétel</option>
                </select>
              </div>

              <div className="form-group">
                <label className="form-label">Hiba részletes leírása *</label>
                <textarea
                  className="form-textarea"
                  rows={4}
                  placeholder="Kérjük, írd le pontosan a tapasztalt hibát..."
                  value={leiras}
                  onChange={(e) => setLeiras(e.target.value)}
                  required
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
                  {isSaving ? 'Beküldés…' : 'Hibajegy beküldése'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
