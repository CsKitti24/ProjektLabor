import { useCallback, useEffect, useState } from 'react'
import { Bell, CalendarClock, Check, RefreshCw } from 'lucide-react'
import client from '../api/client'
import { notifyNotificationsChanged } from '../components/useUnreadNotifications'
import Pagination, { DEFAULT_PAGE_SIZE } from '../components/Pagination'

interface Notification {
  id: number
  foglalasId: number | null
  ertesitesTipus: string
  uzenet?: string | null
  idoPont: string
  allapot: string
  createdAt: string
  foglalasKezdete: string | null
  foglalasVege: string | null
  olvasva: boolean
  kulcsSzam: string | null
  teremNev: string | null
  hatarido: string | null
}

function formatDate(value: string | null): string {
  if (!value) return 'Nem érhető el'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'Ismeretlen időpont'
  return date.toLocaleString('hu-HU', {
    timeZone: 'Europe/Budapest',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function getNotificationTitle(type: string): string {
  if (type === 'BeforeEnd') return 'A foglalásod hamarosan véget ér'
  if (type === 'EveningKeyReturn') return 'Esti kulcsvisszavétel szükséges'
  if (type === 'BookingCancelled') return 'A foglalásodat lemondták'
  if (type === 'BookingChanged') return 'A foglalásod módosult'
  return `Értesítés: ${type}`
}

function getNotificationState(state: string): string {
  switch (state) {
    case 'Kesz':
      return 'Elérhető a felületen'
    case 'KuldesAlatt':
      return 'Feldolgozás alatt'
    case 'Hiba':
      return 'Feldolgozási hiba'
    default:
      return state
  }
}

export default function NotificationsPage() {
  const [notifications, setNotifications] = useState<Notification[] | null>(null)
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [readingId, setReadingId] = useState<number | null>(null)
  const [readError, setReadError] = useState('')

  const loadNotifications = useCallback(async () => {
    setIsLoading(true)
    setError('')
    setNotifications(null)
    try {
      const response = await client.get<Notification[]>('/notifications/my', {
        params: { page, pageSize: DEFAULT_PAGE_SIZE },
      })
      if (!Array.isArray(response.data)) {
        throw new Error('Az értesítések válasza érvénytelen.')
      }
      setNotifications(response.data)
      // Az összes értesítés számát a backend az X-Total-Count fejlécben adja.
      const headerTotal = Number(response.headers?.['x-total-count'])
      setTotalCount(Number.isFinite(headerTotal) && headerTotal > 0 ? headerTotal : response.data.length)
    } catch {
      setError('Az értesítések betöltése nem sikerült. Próbáld újra.')
    } finally {
      setIsLoading(false)
    }
  }, [page])

  const markAsRead = async (id: number) => {
    setReadingId(id)
    setReadError('')
    try {
      await client.patch(`/notifications/${id}/read`)
      notifyNotificationsChanged()
      setNotifications((current) =>
        current?.map((notification) =>
          notification.id === id ? { ...notification, olvasva: true } : notification,
        ) ?? null,
      )
    } catch {
      setReadError('Az értesítés olvasottként jelölése nem sikerült. Próbáld újra.')
    } finally {
      setReadingId(null)
    }
  }

  useEffect(() => {
    void Promise.resolve().then(() => loadNotifications())
  }, [loadNotifications])

  // Az új értesítések percenként, a lista eltüntetése nélkül (háttérben) frissülnek.
  useEffect(() => {
    const timer = window.setInterval(() => {
      client
        .get<Notification[]>('/notifications/my', { params: { page, pageSize: DEFAULT_PAGE_SIZE } })
        .then((response) => {
          if (!Array.isArray(response.data)) return
          setNotifications(response.data)
          const headerTotal = Number(response.headers?.['x-total-count'])
          setTotalCount(Number.isFinite(headerTotal) && headerTotal > 0 ? headerTotal : response.data.length)
        })
        .catch(() => {
          // Háttérfrissítési hiba: a meglévő lista marad, a következő körben újra próbáljuk.
        })
    }, 60_000)
    return () => window.clearInterval(timer)
  }, [page])

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <Bell size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Értesítéseim</span>
          </h1>
          <p className="page-subtitle">A foglalásaidhoz és a kulcsvisszavételhez kapcsolódó rendszerüzenetek</p>
        </div>
      </div>

      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : error ? (
        <div className="alert alert-error" role="alert" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
          <span>{error}</span>
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => void loadNotifications()}>
            <RefreshCw size={14} strokeWidth={1.5} />
            <span>Újrapróbálás</span>
          </button>
        </div>
      ) : notifications?.length === 0 ? (
        <div className="card" style={{ color: 'var(--color-gray-600)' }}>
          Még nincs értesítésed. A foglalási és kulcsvisszavételi értesítések itt jelennek meg.
        </div>
      ) : notifications ? (
        <div style={{ display: 'grid', gap: 12 }}>
          {readError && <div className="alert alert-error" role="alert">{readError}</div>}
          {notifications.map((notification) => {
            const isKeyReturn = notification.ertesitesTipus === 'EveningKeyReturn'
            return (
              <article
                key={notification.id}
                className="card"
                style={{ display: 'grid', gap: 10, borderLeft: notification.olvasva ? undefined : '3px solid var(--color-primary)' }}
              >
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
                  <h2 style={{ margin: 0, fontSize: 15, fontWeight: 600, color: 'var(--color-gray-900)' }}>
                    {getNotificationTitle(notification.ertesitesTipus)}
                  </h2>
                  <div style={{ display: 'flex', gap: 8 }}>
                    <span className="badge badge-primary">{notification.olvasva ? 'Olvasott' : 'Olvasatlan'}</span>
                    <span className="badge badge-primary">{getNotificationState(notification.allapot)}</span>
                  </div>
                </div>
                {isKeyReturn ? (
                  <div style={{ display: 'grid', gap: 6, color: 'var(--color-gray-700)', fontSize: 13 }}>
                    <div><strong>Kulcs:</strong> {notification.kulcsSzam ?? 'Ismeretlen kulcs'}</div>
                    <div><strong>Helyiség:</strong> {notification.teremNev ?? 'Ismeretlen helyiség'}</div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <CalendarClock size={15} strokeWidth={1.5} />
                      <span><strong>Visszavételi határidő:</strong> {formatDate(notification.hatarido)}</span>
                    </div>
                  </div>
                ) : (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: 'var(--color-gray-600)', fontSize: 13 }}>
                    <CalendarClock size={15} strokeWidth={1.5} />
                    <span>
                      Foglalás #{notification.foglalasId ?? '—'}: {formatDate(notification.foglalasKezdete)} – {formatDate(notification.foglalasVege)}
                    </span>
                  </div>
                )}
                {notification.uzenet && (
                  <p style={{ margin: 0, color: 'var(--color-gray-700)', fontSize: 13 }}>{notification.uzenet}</p>
                )}
                <div style={{ color: 'var(--color-gray-500)', fontSize: 12 }}>
                  Értesítés rögzítve: {formatDate(notification.idoPont)}
                </div>
                {!notification.olvasva && (
                  <div>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={readingId === notification.id}
                      onClick={() => void markAsRead(notification.id)}
                    >
                      <Check size={14} strokeWidth={1.5} />
                      <span>{readingId === notification.id ? 'Mentés…' : 'Megjelölés olvasottként'}</span>
                    </button>
                  </div>
                )}
              </article>
            )
          })}
          <Pagination page={page} pageSize={DEFAULT_PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
        </div>
      ) : (
        <div className="alert alert-error" role="alert">Az értesítések nem érhetők el.</div>
      )}
    </div>
  )
}
