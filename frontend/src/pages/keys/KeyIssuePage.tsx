import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { ArrowUpRight, Check, AlertCircle, Key as KeyIcon } from 'lucide-react'
import client from '../../api/client'

interface KeyItem {
  id: number
  szam?: string
  tipus?: string
  teremNev?: string
  jelenlegiAllapot?: string
  aktivalva?: boolean
}

interface UserOption {
  id: number
  nev?: string
  username: string
  email?: string
}

export default function KeyIssuePage() {
  const [keys, setKeys] = useState<KeyItem[]>([])
  const [users, setUsers] = useState<UserOption[]>([])
  const [recipientsForKeyId, setRecipientsForKeyId] = useState('')
  const [keyId, setKeyId] = useState('')
  const [userId, setUserId] = useState('')
  const [pin, setPin] = useState('')
  const [teremAllapot, setTeremAllapot] = useState('')
  const [serulesLeiras, setSerulesLeiras] = useState('')
  const [megjegyzes, setMegjegyzes] = useState('')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const recipientOptions = recipientsForKeyId === keyId ? users : []

  const fetchKeys = useCallback(() => {
    client
      .get<KeyItem[]>('/keys')
      .then((r) => {
        // Csak a szabad és aktív kulcsokat listázzuk kiadásra
        const available = r.data.filter(
          (k) => k.aktivalva !== false && (k.jelenlegiAllapot === 'Szabad' || !k.jelenlegiAllapot)
        )
        setKeys(available)
        if (available.length > 0) {
          setKeyId((prev) => (prev ? prev : String(available[0].id)))
        }
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a kiadható kulcsokat.')
      })
  }, [])

  useEffect(() => {
    fetchKeys()

  }, [fetchKeys])

  useEffect(() => {
    if (!keyId) return

    let ignore = false
    client
      .get<UserOption[]>('/key-movements/recipients', { params: { kulcsId: Number(keyId) } })
      .then((res) => {
        if (!ignore) {
          setError('')
          setUsers(res.data)
          setRecipientsForKeyId(keyId)
          setUserId(res.data.length > 0 ? String(res.data[0].id) : '')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const axErr = err as { response?: { data?: { message?: string } } }
          setError(axErr.response?.data?.message || 'Nem sikerült betölteni a kulcsra jogosult átvevőket.')
        }
      })
    return () => {
      ignore = true
    }
  }, [keyId])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!keyId || !userId || !recipientOptions.some((user) => String(user.id) === userId) ||
      !pin || !teremAllapot.trim()) {
      setError('Válaszd ki a kulcsot és az átvevőt, add meg a PIN-kódot és a terem állapotát.')
      return
    }

    setError('')
    setIsLoading(true)

    try {
      await client.post('/key-movements/issue', {
        kulcsId: Number(keyId),
        atvevoFelhasznaloId: Number(userId),
        pin,
        teremAllapot: teremAllapot.trim(),
        serulesLeiras: serulesLeiras.trim() || undefined,
        megjegyzes: megjegyzes.trim() || undefined,
      })

      setSuccess('A kulcs kiadása sikeresen rögzítve a rendszerben!')
      setPin('')
      setTeremAllapot('')
      setSerulesLeiras('')
      setMegjegyzes('')
      fetchKeys()
      setTimeout(() => setSuccess(''), 4000)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A kulcskiadás meghiúsult.')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <ArrowUpRight size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Kulcskiadás rögzítése</span>
          </h1>
          <p className="page-subtitle">Kulcs átadása PIN-ellenőrzéssel és a terem állapotának rögzítésével</p>
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
            <label className="form-label" htmlFor="issue-key">
              Kiadandó kulcs *
            </label>
            <select
              id="issue-key"
              className="form-select"
              value={keyId}
              onChange={(e) => setKeyId(e.target.value)}
              required
            >
              {keys.length === 0 ? (
                <option value="">Nincs jelenleg szabadon kiadható kulcs</option>
              ) : (
                keys.map((k) => (
                  <option key={k.id} value={k.id}>
                    Kulcs #{k.szam || k.id} – {k.teremNev || 'Általános'} ({k.tipus || 'Osztálykulcs'})
                  </option>
                ))
              )}
            </select>
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="issue-user">
              Jogosult átvevő *
            </label>
            {recipientOptions.length > 0 ? (
              <select
                id="issue-user"
                className="form-select"
                value={userId}
                onChange={(e) => setUserId(e.target.value)}
                required
              >
                {recipientOptions.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.nev ? `${u.nev} (${u.username})` : u.username}
                  </option>
                ))}
              </select>
            ) : (
              <p className="form-hint">Ehhez a kulcshoz nincs beállított átvevői jogosultság.</p>
            )}
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="issue-pin">Az átvevő PIN-kódja *</label>
            <input id="issue-pin" className="form-input" type="password" inputMode="numeric"
              pattern="[0-9]{4,8}" minLength={4} maxLength={8} autoComplete="off"
              value={pin} onChange={(e) => setPin(e.target.value)} required />
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="issue-condition">Terem állapota *</label>
            <textarea id="issue-condition" className="form-textarea" rows={2} maxLength={500}
              placeholder="pl. Rendben, tiszta, felszerelés működik"
              value={teremAllapot} onChange={(e) => setTeremAllapot(e.target.value)} required />
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="issue-damage">Sérülés vagy rendellenesség (opcionális)</label>
            <textarea id="issue-damage" className="form-textarea" rows={2} maxLength={1000}
              value={serulesLeiras} onChange={(e) => setSerulesLeiras(e.target.value)} />
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="issue-note">
              Megjegyzés (opcionális)
            </label>
            <textarea
              id="issue-note"
              className="form-textarea"
              rows={2}
              placeholder="pl. Pótóra megtartására felvéve..."
              value={megjegyzes}
              onChange={(e) => setMegjegyzes(e.target.value)}
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 24 }}>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isLoading || keys.length === 0 || recipientOptions.length === 0}
            >
              {isLoading ? (
                <>
                  <span className="spinner" />
                  <span>Rögzítés…</span>
                </>
              ) : (
                <>
                  <KeyIcon size={15} strokeWidth={1.5} />
                  <span>Kiadás jóváhagyása</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
