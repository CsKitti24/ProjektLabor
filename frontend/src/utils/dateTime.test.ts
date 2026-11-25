import { describe, expect, it } from 'vitest'
import { addDays, formatTimeOnly, isWeekendDate, isoToLocalInput, localDateInputValue, localDateTimeToIso } from './dateTime'

describe('dateTime (Europe/Budapest)', () => {
  it('converts Budapest wall-clock time to UTC in winter and summer', () => {
    expect(localDateTimeToIso('2026-01-15', '08:00')).toBe('2026-01-15T07:00:00.000Z')
    expect(localDateTimeToIso('2026-07-15', '08:00')).toBe('2026-07-15T06:00:00.000Z')
  })

  it('handles the daylight saving switch days', () => {
    expect(localDateTimeToIso('2026-03-29', '08:00')).toBe('2026-03-29T06:00:00.000Z')
    expect(localDateTimeToIso('2026-10-25', '08:00')).toBe('2026-10-25T07:00:00.000Z')
  })

  it('formats and round-trips in Budapest time regardless of the browser time zone', () => {
    expect(isoToLocalInput('2026-07-15T06:00:00.000Z')).toBe('2026-07-15T08:00')
    expect(formatTimeOnly('2026-01-15T07:30:00.000Z')).toBe('08:30')
    // 23:30 UTC on 14 Jan is already 15 Jan in Budapest.
    expect(localDateInputValue(new Date('2026-01-14T23:30:00.000Z'))).toBe('2026-01-15')
  })

  it('works with calendar dates', () => {
    expect(isWeekendDate('2026-11-28')).toBe(true)
    expect(isWeekendDate('2026-11-30')).toBe(false)
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
  })
})
