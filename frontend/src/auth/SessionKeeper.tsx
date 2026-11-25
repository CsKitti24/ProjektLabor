import { useCallback, useEffect, useRef, useState } from 'react'
import { AlertCircle } from 'lucide-react'
import client from '../api/client'
import { formatDateTime } from '../utils/dateTime'

// Hosszú műszakban (pl. portás) a munkamenet ne járjon le munka közben: ha a felhasználó az elmúlt
// negyedórában aktív volt, 5 percenként megújítjuk a tokent (POST /auth/refresh). A megújítás a
// bejelentkezéstől számított felső határig lehetséges (szerveroldali Jwt:MaxSessionHours); a lejárat
// előtt figyelmeztetünk, hogy a félbehagyott munkát még el lehessen menteni.
const REFRESH_INTERVAL_MS = 5 * 60 * 1000
const ACTIVITY_WINDOW_MS = 15 * 60 * 1000
const WARNING_BEFORE_MS = 10 * 60 * 1000
const CHECK_INTERVAL_MS = 30 * 1000

interface RefreshResponse {
  expiresAt?: string
  sessionEndsAt?: string
}

export default function SessionKeeper({ onExpired }: { onExpired: () => void }) {
  const [expiresAt, setExpiresAt] = useState<number | null>(null)
  const [sessionEndsAt, setSessionEndsAt] = useState<number | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const lastActivity = useRef(0)
  const lastRefresh = useRef(0)

  const refresh = useCallback(async () => {
    lastRefresh.current = Date.now()
    try {
      const response = await client.post<RefreshResponse>('/auth/refresh')
      const data = response?.data
      if (data?.expiresAt) setExpiresAt(new Date(data.expiresAt).getTime())
      if (data?.sessionEndsAt) setSessionEndsAt(new Date(data.sessionEndsAt).getTime())
    } catch {
      // Hálózati hiba: a következő körben újrapróbáljuk; 401 esetén a kliens átirányít a belépéshez.
    }
  }, [])

  useEffect(() => {
    const markActive = () => { lastActivity.current = Date.now() }
    const events = ['mousedown', 'keydown', 'touchstart', 'scroll'] as const
    events.forEach((name) => window.addEventListener(name, markActive, { passive: true }))
    // A belépés/oldalbetöltés aktivitásnak számít; az első megújítás azonnal (a következő tickben) indul.
    markActive()
    const initial = window.setTimeout(() => { void refresh() }, 0)

    const timer = window.setInterval(() => {
      const current = Date.now()
      setNow(current)
      if (current - lastActivity.current < ACTIVITY_WINDOW_MS &&
          current - lastRefresh.current >= REFRESH_INTERVAL_MS) {
        void refresh()
      }
    }, CHECK_INTERVAL_MS)

    return () => {
      events.forEach((name) => window.removeEventListener(name, markActive))
      window.clearInterval(timer)
      window.clearTimeout(initial)
    }
  }, [refresh])

  useEffect(() => {
    if (expiresAt !== null && now >= expiresAt) onExpired()
  }, [expiresAt, now, onExpired])

  if (expiresAt === null || expiresAt - now > WARNING_BEFORE_MS) return null

  // Ha a lejárat a munkamenet felső határa, már nem hosszabbítható: újra be kell lépni.
  const atHardLimit = sessionEndsAt !== null && sessionEndsAt - expiresAt < 60 * 1000
  const minutesLeft = Math.max(1, Math.ceil((expiresAt - now) / 60000))

  return (
    <div className="alert alert-error" role="alert" style={{ position: 'fixed', bottom: 16, right: 16, left: 16, maxWidth: 520, marginLeft: 'auto', zIndex: 1000 }}>
      <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
      <span style={{ flex: 1 }}>
        {atHardLimit
          ? `A munkamenet ${formatDateTime(new Date(expiresAt).toISOString())}-kor (kb. ${minutesLeft} perc múlva) lejár. Mentsd a munkádat, majd jelentkezz be újra.`
          : `A munkamenet kb. ${minutesLeft} perc múlva lejár.`}
      </span>
      {!atHardLimit && (
        <button type="button" className="btn btn-secondary btn-sm" onClick={() => void refresh()}>
          Maradok bejelentkezve
        </button>
      )}
    </div>
  )
}
