import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { ArrowUpRight, Check, AlertCircle, Key as KeyIcon } from 'lucide-react'
import client from '../../api/client'
import { formatTimeOnly } from '../../utils/dateTime'

interface KeyItem {
  id: number
  szam?: string
  tipus?: string
  teremNev?: string
  jelenlegiAllapot?: string
  aktivalva?: boolean
  teremAktiv?: boolean
}

interface UserOption {
  id: number
  nev?: string
  username: string
  email?: string
  hasPin?: boolean
  // Ha az oktatónak most esedékes foglalása van a kulcs termére: a foglalás kezdete.
  esedekesFoglalasKezdete?: string | null
}

interface DueBooking {
  foglalasId: number
  felhasznaloId: number
  felhasznaloNev: string
  teremNev: string
  startTime: string
  endTime: string
}

interface RoomOption {
  id: number
  nev: string
}

export default function KeyIssuePage() {
  const [keys, setKeys] = useState<KeyItem[]>([])
  const [roomsForMasterKey, setRoomsForMasterKey] = useState<RoomOption[]>([])
  const [roomsForKeyId, setRoomsForKeyId] = useState('')
  const [users, setUsers] = useState<UserOption[]>([])
  const [recipientsForKeyId, setRecipientsForKeyId] = useState('')
  const [keyId, setKeyId] = useState('')
  const [userId, setUserId] = useState('')
  const [roomId, setRoomId] = useState('')
  const [pin, setPin] = useState('')
  const [teremAllapot, setTeremAllapot] = useState('')
  const [serulesLeiras, setSerulesLeiras] = useState('')
  const [megjegyzes, setMegjegyzes] = useState('')
  const [recipientSearch, setRecipientSearch] = useState('')
  // Helyettesítő átvevő: a kulcsot nem a foglaló, hanem egy kolléga veszi fel ennek a foglalásnak.
  const [dueBookings, setDueBookings] = useState<DueBooking[]>([])
  const [substituteBookingId, setSubstituteBookingId] = useState('')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const recipientOptions = recipientsForKeyId === keyId ? users : []
  const selectedKey = keys.find((key) => String(key.id) === keyId)

  const fetchKeys = useCallback(() => {
    client
      .get<KeyItem[]>('/keys')
      .then((r) => {
        // Csak a szabad és aktív kulcsokat listázzuk kiadásra
        // Csak a szabad, aktív és aktív teremhez tartozó kulcsok adhatók ki.
        const available = r.data.filter(
          (k) => k.aktivalva !== false && k.teremAktiv !== false &&
            (k.jelenlegiAllapot === 'Szabad' || !k.jelenlegiAllapot)
        )
        setKeys(available)
        // Ha a korábban kiválasztott kulcs már nem adható ki (pl. épp most adtuk ki), az első szabadra váltunk.
        setKeyId((prev) => (available.some((k) => String(k.id) === prev) ? prev : String(available[0]?.id ?? '')))
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
    if (selectedKey?.tipus !== 'Mesterkulcs') return

    client.get<RoomOption[]>(`/keys/${keyId}/rooms`)
      .then((response) => {
        if (!ignore) {
          setRoomsForMasterKey(response.data)
          setRoomsForKeyId(keyId)
          setRoomId(response.data.length > 0 ? String(response.data[0].id) : '')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const axErr = err as { response?: { data?: { message?: string } } }
          setError(axErr.response?.data?.message || 'Nem sikerült betölteni a mesterkulcs termeit.')
        }
      })
    return () => {
      ignore = true
    }
  }, [keyId, selectedKey?.tipus])

  useEffect(() => {
    if (!keyId) return

    let ignore = false
    client
      .get<UserOption[]>('/key-movements/recipients', { params: { kulcsId: Number(keyId) } })
      .then((res) => {
        if (!ignore) {
          setUsers(res.data)
          setRecipientsForKeyId(keyId)
          setRecipientSearch('')
          setSubstituteBookingId('')
          // Alapértelmezett átvevő: aki most esedékes foglalással érkezik (és van PIN-je); egyébként választani kell.
          const dueRecipient = res.data.find((user) => user.esedekesFoglalasKezdete && user.hasPin !== false)
          setUserId(dueRecipient ? String(dueRecipient.id) : '')
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

  useEffect(() => {
    if (!keyId) return
    let ignore = false
    client
      .get<DueBooking[]>('/key-movements/due-bookings', { params: { kulcsId: Number(keyId) } })
      .then((res) => {
        if (!ignore) setDueBookings(Array.isArray(res.data) ? res.data : [])
      })
      .catch(() => {
        if (!ignore) setDueBookings([])
      })
    return () => {
      ignore = true
    }
  }, [keyId])

  const searchTerm = recipientSearch.trim().toLocaleLowerCase('hu')
  const visibleRecipients = recipientOptions.filter((user) =>
    !searchTerm ||
    String(user.id) === userId ||
    `${user.nev ?? ''} ${user.username}`.toLocaleLowerCase('hu').includes(searchTerm))
  // Helyettesítés akkor lehetséges, ha van esedékes foglalás, de a kiválasztott átvevő nem foglaló.
  const substituteCandidates = userId
    ? dueBookings.filter((booking) => String(booking.felhasznaloId) !== userId)
    : []

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!keyId || !userId || !recipientOptions.some((user) => String(user.id) === userId) ||
      !pin || !teremAllapot.trim() ||
      (selectedKey?.tipus === 'Mesterkulcs' && (!roomId || roomsForKeyId !== keyId))) {
      setError('Válaszd ki a kulcsot és az átvevőt, add meg a PIN-kódot és a terem állapotát.')
      return
    }

    setError('')
    setIsLoading(true)

    try {
      const response = await client.post<{ foglalasId?: number | null }>('/key-movements/issue', {
        kulcsId: Number(keyId),
        atvevoFelhasznaloId: Number(userId),
        foglalasId: substituteBookingId ? Number(substituteBookingId) : undefined,
        teremId: selectedKey?.tipus === 'Mesterkulcs' ? Number(roomId) : undefined,
        pin,
        teremAllapot: teremAllapot.trim(),
        serulesLeiras: serulesLeiras.trim() || undefined,
        megjegyzes: megjegyzes.trim() || undefined,
      })

      const bookingId = response.data?.foglalasId
      setSuccess(
        bookingId
          ? `A kulcs kiadása rögzítve, és az átvevő #${bookingId} foglalásához kapcsolódik.`
          : 'A kulcs kiadása rögzítve. Az átvevőnek nincs erre a teremre szóló, most esedékes foglalása.'
      )
      setPin('')
      setTeremAllapot('')
      setSerulesLeiras('')
      setMegjegyzes('')
      setSubstituteBookingId('')
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
              onChange={(e) => {
                setKeyId(e.target.value)
                setError('')
              }}
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

          {selectedKey?.tipus === 'Mesterkulcs' && (
            <div className="form-group">
              <label className="form-label" htmlFor="issue-room">Érintett terem *</label>
              {roomsForKeyId !== keyId ? (
                <p className="form-hint">A mesterkulcs termeinek betöltése…</p>
              ) : roomsForMasterKey.length > 0 ? (
                <select
                  id="issue-room"
                  className="form-select"
                  value={roomId}
                  onChange={(event) => setRoomId(event.target.value)}
                  required
                >
                  {roomsForMasterKey.map((room) => (
                    <option key={room.id} value={room.id}>{room.nev}</option>
                  ))}
                </select>
              ) : (
                <p className="form-hint">Ehhez a mesterkulcshoz nincs terem rendelve.</p>
              )}
            </div>
          )}

          <div className="form-group">
            <label className="form-label" htmlFor="issue-user">
              Jogosult átvevő *
            </label>
            {recipientOptions.length > 0 ? (
              <>
                {recipientOptions.length > 8 && (
                  <input
                    className="form-input"
                    style={{ marginBottom: 6 }}
                    aria-label="Átvevő keresése"
                    placeholder="Keresés név vagy felhasználónév szerint…"
                    value={recipientSearch}
                    onChange={(e) => setRecipientSearch(e.target.value)}
                  />
                )}
                <select
                  id="issue-user"
                  className="form-select"
                  value={userId}
                  onChange={(e) => {
                    setUserId(e.target.value)
                    setSubstituteBookingId('')
                  }}
                  required
                >
                  <option value="">Válassz átvevőt…</option>
                  {visibleRecipients.map((u) => (
                    <option key={u.id} value={u.id} disabled={u.hasPin === false}>
                      {u.nev ? `${u.nev} (${u.username})` : u.username}
                      {u.esedekesFoglalasKezdete ? ` – foglalása: ${formatTimeOnly(u.esedekesFoglalasKezdete)}` : ''}
                      {u.hasPin === false ? ' – nincs PIN-kódja' : ''}
                    </option>
                  ))}
                </select>
              </>
            ) : (
              <p className="form-hint">
                {keyId
                  ? 'Ehhez a kulcshoz nincs beállított átvevői jogosultság.'
                  : 'Előbb válassz kiadható kulcsot.'}
              </p>
            )}
          </div>

          {substituteCandidates.length > 0 && (
            <div className="form-group">
              <label className="form-label" htmlFor="issue-substitute">Helyettesítőként veszi fel? (opcionális)</label>
              <select
                id="issue-substitute"
                className="form-select"
                value={substituteBookingId}
                onChange={(e) => setSubstituteBookingId(e.target.value)}
              >
                <option value="">Nem, a saját nevén veszi fel</option>
                {substituteCandidates.map((booking) => (
                  <option key={booking.foglalasId} value={booking.foglalasId}>
                    {booking.felhasznaloNev} foglalásához ({booking.teremNev}, {formatTimeOnly(booking.startTime)}–{formatTimeOnly(booking.endTime)})
                  </option>
                ))}
              </select>
              <p className="form-hint">
                Helyettesítéskor a foglalás nem lesz NoShow, a késésért járó pont pedig a foglalót terheli.
              </p>
            </div>
          )}

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
              disabled={isLoading || keys.length === 0 || recipientOptions.length === 0 ||
                (selectedKey?.tipus === 'Mesterkulcs' &&
                  (!roomId || roomsForKeyId !== keyId))}
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
