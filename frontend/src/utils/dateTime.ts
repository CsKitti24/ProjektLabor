// A backend minden időpontot UTC-ben tárol és "Z" jelöléssel ad vissza, a szabályokat (nyitvatartás,
// 45 perces sávok, hétvége) pedig az intézmény helyi ideje, Europe/Budapest szerint ellenőrzi.
// Ezért a felület is mindig budapesti idő szerint számol és ír ki – a böngésző (pl. rosszul beállított
// vagy külföldi) időzónájától függetlenül.

export const APP_TIME_ZONE = 'Europe/Budapest'

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

const partsFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: APP_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
})

interface ZonedParts {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second: number
}

/** Egy időpont budapesti faliórája (év, hónap, nap, óra, perc, másodperc). */
function zonedParts(epochMs: number): ZonedParts {
  const values: Record<string, number> = {}
  for (const part of partsFormatter.formatToParts(new Date(epochMs))) {
    if (part.type !== 'literal') values[part.type] = Number(part.value)
  }
  return {
    year: values.year,
    month: values.month,
    day: values.day,
    hour: values.hour === 24 ? 0 : values.hour,
    minute: values.minute,
    second: values.second,
  }
}

/** A budapesti idő eltérése az UTC-től az adott pillanatban (ezredmásodperc). */
function zoneOffsetMs(epochMs: number): number {
  const p = zonedParts(epochMs)
  const wallAsUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second)
  return wallAsUtc - Math.floor(epochMs / 1000) * 1000
}

/** A budapesti naptári nap YYYY-MM-DD formában (nem a böngésző és nem az UTC szerinti nap). */
export function localDateInputValue(date: Date = new Date()): string {
  const p = zonedParts(date.getTime())
  return `${p.year}-${pad(p.month)}-${pad(p.day)}`
}

/** Budapesti dátum (YYYY-MM-DD) és idő (HH:mm) UTC ISO-időbélyeggé alakítása. */
export function localDateTimeToIso(date: string, time: string): string {
  const [year, month, day] = date.split('-').map(Number)
  const [hour, minute] = time.split(':').map(Number)
  const wallAsUtc = Date.UTC(year, month - 1, day, hour, minute)
  // Két lépésben, hogy a nyári/téli időszámítás váltása körül is a helyes eltolást használjuk.
  let utc = wallAsUtc - zoneOffsetMs(wallAsUtc)
  utc = wallAsUtc - zoneOffsetMs(utc)
  return new Date(utc).toISOString()
}

/** Egy datetime-local mező értéke (YYYY-MM-DDTHH:mm, budapesti idő) UTC ISO-időbélyeggé alakítva. */
export function localInputToIso(value: string): string {
  const [date, time = '00:00'] = value.split('T')
  return localDateTimeToIso(date, time.slice(0, 5))
}

/** UTC ISO-időbélyeg budapesti datetime-local mezőértékként (YYYY-MM-DDTHH:mm). */
export function isoToLocalInput(iso: string): string {
  const p = zonedParts(new Date(iso).getTime())
  return `${p.year}-${pad(p.month)}-${pad(p.day)}T${pad(p.hour)}:${pad(p.minute)}`
}

/** A hét napja egy YYYY-MM-DD dátumhoz (0 = vasárnap, 6 = szombat), időzónától függetlenül. */
export function dayOfWeek(date: string): number {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day)).getUTCDay()
}

export function isWeekendDate(date: string): boolean {
  const day = dayOfWeek(date)
  return day === 0 || day === 6
}

/** YYYY-MM-DD dátumhoz napok hozzáadása (naptári napként, időzónától függetlenül). */
export function addDays(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number)
  const shifted = new Date(Date.UTC(year, month - 1, day + days))
  return `${shifted.getUTCFullYear()}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}`
}

/** Dátum és idő kiírása budapesti idő szerint, magyar formátumban. */
export function formatDateTime(iso: string | undefined | null, options: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
}): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleString('hu-HU', { ...options, timeZone: APP_TIME_ZONE })
}

export function formatDateOnly(iso: string | undefined | null): string {
  return formatDateTime(iso, { year: 'numeric', month: '2-digit', day: '2-digit' })
}

export function formatTimeOnly(iso: string | undefined | null): string {
  return formatDateTime(iso, { hour: '2-digit', minute: '2-digit' })
}

// Napokig tartó késésnél is olvasható formátum, pl. "4 nap 12 óra" vagy "35 perc".
export function formatDelay(minutes: number): string {
  const total = Math.max(1, Math.round(minutes))
  const days = Math.floor(total / 1440)
  const hours = Math.floor((total % 1440) / 60)
  const mins = total % 60
  if (days > 0) return hours > 0 ? `${days} nap ${hours} óra` : `${days} nap`
  if (hours > 0) return mins > 0 ? `${hours} óra ${mins} perc` : `${hours} óra`
  return `${mins} perc`
}
