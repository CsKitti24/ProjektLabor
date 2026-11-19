// A backend minden időpontot UTC-ben tárol és "Z" jelöléssel ad vissza.
// Ezek a segédfüggvények a böngésző helyi ideje és az UTC ISO-formátum között váltanak.

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** A helyi naptári nap YYYY-MM-DD formában (nem az UTC szerinti nap). */
export function localDateInputValue(date: Date = new Date()): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** Helyi dátum (YYYY-MM-DD) és idő (HH:mm) UTC ISO-időbélyeggé alakítása. */
export function localDateTimeToIso(date: string, time: string): string {
  return new Date(`${date}T${time}:00`).toISOString()
}

/** Egy datetime-local mező értéke (YYYY-MM-DDTHH:mm) UTC ISO-időbélyeggé alakítva. */
export function localInputToIso(value: string): string {
  return new Date(value).toISOString()
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
