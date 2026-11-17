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
  kiadottTeremIsmeretlen?: boolean
}

interface KeyRoom {
  id: number
  nev: string
}

export default function KeyReturnPage() {
  const [issuedKeys, setIssuedKeys] = useState<KeyItem[]>([])
  const [keyId, setKeyId] = useState('')
  const [pin, setPin] = useState('')
  const [teremAllapot, setTeremAllapot] = useState('')
  const [serulesLeiras, setSerulesLeiras] = useState('')
  const [megjegyzes, setMegjegyzes] = useState('')
  // Régi, foglalás nélküli mesterkulcs-kiadásnál a kiadás terme nem ismert: ilyenkor a portás választja ki.
  const [roomsByKey, setRoomsByKey] = useState<Record<string, KeyRoom[]>>({})
  const [returnRoomId, setReturnRoomId] = useState('')
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
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a kint lévő kulcsokat.')
      })
  }

  useEffect(() => {
    fetchIssuedKeys()
  }, [])

  const selectedKey = issuedKeys.find((k) => String(k.id) === keyId)
  const needsRoom = Boolean(selectedKey?.kiadottTeremIsmeretlen)

  const roomOptions = roomsByKey[keyId] ?? []

  useEffect(() => {
    if (!needsRoom || !keyId || roomsByKey[keyId]) {
      return
    }

    let ignore = false
    client
      .get<KeyRoom[]>(`/keys/${keyId}/rooms`)
      .then((r) => {
        if (!ignore) setRoomsByKey((prev) => ({ ...prev, [keyId]: r.data }))
      })
      .catch(() => {
        if (!ignore) setError('Nem sikerült betölteni a mesterkulcshoz rendelt termeket.')
      })
    return () => {
      ignore = true
    }
  }, [keyId, needsRoom, roomsByKey])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!keyId || !pin || !teremAllapot.trim()) {
      setError('Válaszd ki a kulcsot, add meg az átvevő PIN-kódját és a terem állapotát.')
      return
    }
    if (needsRoom && !returnRoomId) {
      setError('Válaszd ki, melyik teremből hozták vissza a mesterkulcsot.')
      return
    }

    setError('')
    setIsLoading(true)

    try {
      await client.post('/key-movements/return', {
        kulcsId: Number(keyId),
        teremId: needsRoom ? Number(returnRoomId) : undefined,
        pin,
        teremAllapot: teremAllapot.trim(),
        serulesLeiras: serulesLeiras.trim() || undefined,
        megjegyzes: megjegyzes.trim() || undefined,
      })

      setSuccess('A kulcs sikeresen visszavételezve a portára!')
      setPin('')
      setTeremAllapot('')
      setSerulesLeiras('')
      setMegjegyzes('')
      setReturnRoomId('')
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
          <p className="page-subtitle">Kiadott kulcs visszavétele PIN-ellenőrzéssel és állapotrögzítéssel</p>
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
              onChange={(e) => {
                setKeyId(e.target.value)
                setReturnRoomId('')
              }}
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

          {needsRoom && (
            <div className="form-group">
              <label className="form-label" htmlFor="return-room">
                Melyik teremből hozták vissza? *
              </label>
              <select
                id="return-room"
                className="form-select"
                value={returnRoomId}
                onChange={(e) => setReturnRoomId(e.target.value)}
                required
              >
                <option value="">Válassz termet…</option>
                {roomOptions.map((room) => (
                  <option key={room.id} value={room.id}>{room.nev}</option>
                ))}
              </select>
              <p style={{ fontSize: 12, color: 'var(--color-gray-500)', marginTop: 4 }}>
                Ezt a mesterkulcsot még a terem rögzítése előtt adták ki, ezért a kiadás terme nem ismert.
              </p>
            </div>
          )}

          <div className="form-group">
            <label className="form-label" htmlFor="return-pin">Az átvevő PIN-kódja *</label>
            <input id="return-pin" className="form-input" type="password" inputMode="numeric"
              pattern="[0-9]{4,8}" minLength={4} maxLength={8} autoComplete="off"
              value={pin} onChange={(e) => setPin(e.target.value)} required />
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="return-condition">Terem állapota *</label>
            <textarea id="return-condition" className="form-textarea" rows={2} maxLength={500}
              placeholder="pl. Rendben, tiszta, felszerelés működik"
              value={teremAllapot} onChange={(e) => setTeremAllapot(e.target.value)} required />
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="return-damage">Sérülés vagy rendellenesség (opcionális)</label>
            <textarea id="return-damage" className="form-textarea" rows={2} maxLength={1000}
              value={serulesLeiras} onChange={(e) => setSerulesLeiras(e.target.value)} />
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
