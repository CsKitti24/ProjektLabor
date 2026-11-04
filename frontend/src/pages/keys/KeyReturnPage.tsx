import { useState, useEffect, type FormEvent } from 'react'
import { ArrowDownLeft, Check, AlertCircle, Key as KeyIcon } from 'lucide-react'
import client from '../../api/client'

interface KeyItem {
  id: number
  szam?: string
  tipus?: string
  teremNev?: string
  jelenlegiAllapot?: string
  aktivalva?: boolean
}

export default function KeyReturnPage() {
  const [issuedKeys, setIssuedKeys] = useState<KeyItem[]>([])
  const [keyId, setKeyId] = useState('')
  const [megjegyzes, setMegjegyzes] = useState('')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [isLoading, setIsLoading] = useState(false)

  const fetchIssuedKeys = () => {
    client
      .get<KeyItem[]>('/keys')
      .then((r) => {
        // A kiadott állapotú kulcsokat választjuk ki
        const issued = r.data.filter((k) => k.jelenlegiAllapot === 'Kiadva')
        setIssuedKeys(issued)
        if (issued.length > 0) {
          setKeyId(String(issued[0].id))
        } else {
          setKeyId('')
        }
      })
      .catch(() => {})
  }

  useEffect(() => {
    fetchIssuedKeys()
  }, [])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!keyId) {
      setError('Kérjük, válaszd ki a visszavenni kívánt kulcsot!')
      return
    }

    setError('')
    setIsLoading(true)

    try {
      await client.post('/key-movements/return', {
        kulcsId: Number(keyId),
        megjegyzes: megjegyzes.trim() || undefined,
      })

      setSuccess('A kulcs sikeresen visszavételezve a portára!')
      setMegjegyzes('')
      fetchIssuedKeys()
      setTimeout(() => setSuccess(''), 4000)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A kulcsvisszavétel meghiúsult.')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <ArrowDownLeft size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Kulcsvisszavétel</span>
          </h1>
          <p className="page-subtitle">Kiadott kulcs visszavétele és lezárása</p>
        </div>
      </div>

      <div className="card" style={{ maxWidth: 520 }}>
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
            <label className="form-label" htmlFor="return-key">
              Visszavett kulcs kiválasztása *
            </label>
            <select
              id="return-key"
              className="form-select"
              value={keyId}
              onChange={(e) => setKeyId(e.target.value)}
              required
            >
              {issuedKeys.length === 0 ? (
                <option value="">Nincs jelenleg kint lévő (kiadott) kulcs</option>
              ) : (
                issuedKeys.map((k) => (
                  <option key={k.id} value={k.id}>
                    Kulcs #{k.szam || k.id} – {k.teremNev || 'Általános'} ({k.tipus || 'Osztálykulcs'})
                  </option>
                ))
              )}
            </select>
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="return-note">
              Megjegyzés az állapotról (opcionális)
            </label>
            <textarea
              id="return-note"
              className="form-textarea"
              rows={2}
              placeholder="pl. Sértetlen állapotban átvéve..."
              value={megjegyzes}
              onChange={(e) => setMegjegyzes(e.target.value)}
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 24 }}>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isLoading || issuedKeys.length === 0}
            >
              {isLoading ? (
                <>
                  <span className="spinner" />
                  <span>Rögzítés…</span>
                </>
              ) : (
                <>
                  <KeyIcon size={15} strokeWidth={1.5} />
                  <span>Visszavétel jóváhagyása</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
