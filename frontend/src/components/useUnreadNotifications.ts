import { useEffect, useState } from 'react'
import client from '../api/client'

export const NOTIFICATIONS_CHANGED_EVENT = 'notifications-changed'
const REFRESH_INTERVAL_MS = 60_000

/** Jelzi a többi komponensnek (pl. az oldalsáv jelvényének), hogy az értesítések olvasottsága változott. */
export function notifyNotificationsChanged() {
  window.dispatchEvent(new Event(NOTIFICATIONS_CHANGED_EVENT))
}

/**
 * Az olvasatlan értesítések száma; percenként és olvasottság-változáskor frissül.
 * Csak az értesítéseket fogadó szerepköröknél (oktató, portás) kérdezi le.
 */
export function useUnreadNotifications(enabled: boolean): number {
  const [count, setCount] = useState(0)

  useEffect(() => {
    if (!enabled) return
    let ignore = false
    const load = () => {
      client
        .get('/notifications/my', { params: { csakOlvasatlan: true, page: 1, pageSize: 1 } })
        .then((response) => {
          if (ignore) return
          const total = Number(response.headers?.['x-total-count'])
          setCount(Number.isFinite(total) ? total : 0)
        })
        .catch(() => {
          // A jelvény nem kritikus: hiba esetén a korábbi értéket tartjuk meg.
        })
    }

    load()
    const timer = window.setInterval(load, REFRESH_INTERVAL_MS)
    window.addEventListener(NOTIFICATIONS_CHANGED_EVENT, load)
    return () => {
      ignore = true
      window.clearInterval(timer)
      window.removeEventListener(NOTIFICATIONS_CHANGED_EVENT, load)
    }
  }, [enabled])

  return enabled ? count : 0
}
