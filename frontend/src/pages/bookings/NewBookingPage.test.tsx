// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import NewBookingPage from './NewBookingPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return {
    data,
    status: 200,
    statusText: 'OK',
    headers: new AxiosHeaders(),
    config: { headers: new AxiosHeaders() },
  }
}

describe('NewBookingPage time slots', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockResolvedValue(
      axiosResponse([{ id: 1, nev: 'Tesztterem', kapacitas: 30, aktivalva: true }]),
    )
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('offers only fixed slots aligned to the 45-minute timetable grid', async () => {
    render(
      <MemoryRouter>
        <NewBookingPage />
      </MemoryRouter>,
    )

    await screen.findByRole('option', { name: /Tesztterem/ })
    const timeSlotSelect = screen.getAllByRole('combobox')[1] as HTMLSelectElement
    const ranges = Array.from(timeSlotSelect.options, (option) =>
      option.textContent?.match(/(\d{2}):(\d{2}) – (\d{2}):(\d{2})/),
    )

    expect(ranges).toHaveLength(8)
    for (const range of ranges) {
      expect(range).not.toBeNull()
      if (!range) continue

      const start = Number(range[1]) * 60 + Number(range[2])
      const end = Number(range[3]) * 60 + Number(range[4])
      expect((start - 8 * 60) % 45).toBe(0)
      expect((end - start) % 45).toBe(0)
      expect(end).toBeLessThanOrEqual(20 * 60)
    }
  })

  it('constrains custom times to the same 45-minute grid and operating hours', async () => {
    render(
      <MemoryRouter>
        <NewBookingPage />
      </MemoryRouter>,
    )
    await screen.findByRole('option', { name: /Tesztterem/ })

    fireEvent.click(screen.getByRole('button', { name: 'Egyéni időpont beállítása' }))
    await waitFor(() => {
      expect(screen.getAllByDisplayValue('08:00')).toHaveLength(1)
      expect(screen.getAllByDisplayValue('09:30')).toHaveLength(1)
    })

    const [start, end] = screen.getAllByDisplayValue(/^(08:00|09:30)$/) as HTMLInputElement[]
    expect(start.min).toBe('08:00')
    expect(start.max).toBe('19:15')
    expect(start.step).toBe('2700')
    expect(end.min).toBe('08:45')
    expect(end.max).toBe('20:00')
    expect(end.step).toBe('2700')
  })
})
