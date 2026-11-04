import { useState, useEffect, type FormEvent } from 'react'
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
  const [keyId, setKeyId] = useState('')
  const [userId, setUserId] = useState('')
  const [megjegyzes, setMegjegyzes] = useState('')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [isLoading, setIsLoading] = useState(false)

  const fetchKeys = () => {
    client
      .get<KeyItem[]>('/keys')
      .then((r) => {
        // Csak a szabad és aktív kulcsokat listázzuk kiadásra
        const available = r.data.filter(
          (k) => k.aktivalva !== false && (k.jelenlegiAllapot === 'Szabad' || !k.jelenlegiAllapot)
        )
        setKeys(available)
        if (available.length > 0 && !keyId) {
          setKeyId(String(available[0].id))
        }
      })
      .catch(() => {})
  }

  useEffect(() => {
    fetchKeys()

    // Oktatók / felhasználók lekérése az átvevő kiválasztásához
    client
      .get<{ items?: UserOption[] } | UserOption[]>('/users')
      .then((res) => {
        const userList = Array.isArray(res.data) ? res.data : (res.data?.items ?? [])
        setUsers(userList)
        if (userList.length > 0) {
          setUserId(String(userList[0].id))
        }
      })
      .catch(() => {})
  }, [])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!keyId || !userId) {
      setError('Kérjük, válaszd ki a kulcsot és az átvevő személyt!')
      return
    }

    setError('')
    setIsLoading(true)

    try {
      await client.post('/key-movements/issue', {
        kulcsId: Number(keyId),
        atvevoFelhasznaloId: Number(userId),
        megjegyzes: megjegyzes.trim() || undefined,
      })

      setSuccess('A kulcs kiadása sikeresen rögzítve a rendszerben!')
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
          <p className="page-subtitle">Kulcs átadása oktatónak vagy jogosult személynek</p>
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
              Átvevő személy *
            </label>
            {users.length > 0 ? (
              <select
                id="issue-user"
                className="form-select"
                value={userId}
                onChange={(e) => setUserId(e.target.value)}
                required
              >
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.nev ? `${u.nev} (${u.username})` : u.username}
                  </option>
                ))}
              </select>
            ) : (
              <input
                id="issue-user"
                type="number"
                className="form-input"
                placeholder="Átvevő felhasználó azonosítója (ID)"
                value={userId}
                onChange={(e) => setUserId(e.target.value)}
                required
              />
            )}
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
              disabled={isLoading || keys.length === 0}
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
