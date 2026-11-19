import { describe, expect, it } from 'vitest'
import { formatDelay } from './dateTime'

describe('formatDelay', () => {
  it('formats long delays in days and hours instead of raw minutes', () => {
    expect(formatDelay(6523)).toBe('4 nap 12 óra')
    expect(formatDelay(1440)).toBe('1 nap')
    expect(formatDelay(95)).toBe('1 óra 35 perc')
    expect(formatDelay(0.4)).toBe('1 perc')
  })
})
