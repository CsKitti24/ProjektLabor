import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { AlertCircle, X } from 'lucide-react'
import client from '../../api/client'
import DialogKeyboard from '../../components/DialogKeyboard'
import { formatDateTime } from '../../utils/dateTime'

interface ReliabilityEvent {
  id: number
  esemenyTipus: string
  pont: number
  eredetiHatarido?: string | null
  esemenyIdopontja: string
  tanev: string
  indoklas?: string | null
}

interface ReliabilityDetailsModalProps {
  instructor: { userId: number; nev: string }
  tanev: string
  // Kézi pontkorrekciót csak az adminisztrátor rögzíthet; az üzemeltetési igazgató csak megtekint.
  canCorrect: boolean
  onClose: () => void
  onCorrected: () => void
}

const EVENT_LABELS: Record<string, string> = {
  LeNemAdott: 'A kulcs a napi zárásig nem került vissza',
  'Késedelmesen leadott': 'Késedelmes leadás',
  'Manuális korrekció': 'Kézi korrekció',
}

function errorMessage(err: unknown, fallback: string): string {
  const axErr = err as { response?: { data?: { message?: string } } }
  return axErr.response?.data?.message || fallback
}

export default function ReliabilityDetailsModal({ instructor, tanev, canCorrect, onClose, onCorrected }: ReliabilityDetailsModalProps) {
  const [events, setEvents] = useState<ReliabilityEvent[] | null>(null)
  const [showAllYears, setShowAllYears] = useState(false)
  const [error, setError] = useState('')
  const [points, setPoints] = useState('')
  const [reason, setReason] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [successMsg, setSuccessMsg] = useState('')

  const loadEvents = useCallback(async () => {
    try {
      const response = await client.get<ReliabilityEvent[]>(`/reliability/users/${instructor.userId}/events`)
      setEvents(Array.isArray(response.data) ? response.data : [])
    } catch (err: unknown) {
      setEvents([])
      setError(errorMessage(err, 'Az események betöltése nem sikerült.'))
    }
  }, [instructor.userId])

  useEffect(() => {
    void Promise.resolve().then(() => loadEvents())
  }, [loadEvents])

  const visibleEvents = (events ?? []).filter((item) => showAllYears || item.tanev === tanev)

  const handleCorrection = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setSuccessMsg('')
    const value = Number(points)
    if (!Number.isInteger(value) || value === 0 || value < -100 || value > 100) {
      setError('A korrekció -100 és 100 közötti, nullától eltérő egész szám lehet (a negatív csökkenti a büntetőpontot).')
      return
    }
    if (!reason.trim()) {
      setError('A korrekcióhoz indoklás szükséges.')
      return
    }

    setIsSaving(true)
    try {
      await client.post('/reliability/correction', { userId: instructor.userId, pont: value, indoklas: reason.trim() })
      setPoints('')
      setReason('')
      setSuccessMsg('A korrekció rögzítve.')
      await loadEvents()
      onCorrected()
    } catch (err: unknown) {
      setError(errorMessage(err, 'A korrekció rögzítése nem sikerült.'))
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <DialogKeyboard onClose={onClose} />
      <div className="modal" style={{ maxWidth: 680 }} role="dialog" aria-modal="true" aria-labelledby="reliability-details-title">
        <div className="modal-header">
          <div>
            <h2 className="modal-title" id="reliability-details-title">{instructor.nev} – megbízhatósági események</h2>
            <p style={{ fontSize: 12.5, color: 'var(--color-gray-500)', marginTop: 2 }}>{tanev}. tanév</p>
          </div>
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
        {successMsg && <div className="alert alert-success" role="status">{successMsg}</div>}

        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, marginBottom: 8 }}>
          <input type="checkbox" checked={showAllYears} onChange={(e) => setShowAllYears(e.target.checked)} />
          Korábbi tanévek eseményei is
        </label>

        <div className="table-container" style={{ maxHeight: 280, overflowY: 'auto' }}>
          <table className="data-table" aria-label="Megbízhatósági események">
            <thead>
              <tr>
                <th>Időpont</th>
                <th>Esemény</th>
                <th style={{ textAlign: 'right' }}>Pont</th>
                <th>Indoklás</th>
              </tr>
            </thead>
            <tbody>
              {events === null ? (
                <tr><td colSpan={4} style={{ textAlign: 'center', padding: 20 }}><span className="spinner spinner-primary" /></td></tr>
              ) : visibleEvents.length === 0 ? (
                <tr><td colSpan={4} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 20 }}>Nincs rögzített esemény.</td></tr>
              ) : (
                visibleEvents.map((item) => (
                  <tr key={item.id}>
                    <td>{formatDateTime(item.esemenyIdopontja)}</td>
                    <td>{EVENT_LABELS[item.esemenyTipus] ?? item.esemenyTipus}</td>
                    <td style={{ textAlign: 'right', fontWeight: 600 }}>{item.pont > 0 ? `+${item.pont}` : item.pont}</td>
                    <td style={{ color: 'var(--color-gray-600)', fontSize: 13 }}>{item.indoklas || '—'}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {canCorrect && (
          <form onSubmit={(event) => void handleCorrection(event)} noValidate style={{ marginTop: 16 }}>
            <h3 style={{ fontSize: 14, fontWeight: 600, marginBottom: 8 }}>Kézi pontkorrekció</h3>
            <div style={{ display: 'grid', gridTemplateColumns: '120px 1fr', gap: 12 }}>
              <div className="form-group">
                <label className="form-label" htmlFor="correction-points">Pont (±)</label>
                <input id="correction-points" type="number" min={-100} max={100} step={1} className="form-input"
                  placeholder="pl. -3" value={points} onChange={(e) => setPoints(e.target.value)} />
              </div>
              <div className="form-group">
                <label className="form-label" htmlFor="correction-reason">Indoklás</label>
                <input id="correction-reason" className="form-input" maxLength={500}
                  placeholder="pl. Igazolt technikai hiba miatt nem tudta visszahozni" value={reason}
                  onChange={(e) => setReason(e.target.value)} />
              </div>
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button type="submit" className="btn btn-primary btn-sm" disabled={isSaving}>
                {isSaving ? 'Rögzítés…' : 'Korrekció rögzítése'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
