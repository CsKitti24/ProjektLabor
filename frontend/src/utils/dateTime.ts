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
