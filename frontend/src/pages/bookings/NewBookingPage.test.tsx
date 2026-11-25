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

const mockAuth = vi.hoisted(() => ({ role: 'Oktato' }))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({ user: { userId: 1, username: 'u', role: mockAuth.role } }),
}))

function nextWeekday(): string {
  const date = new Date()
  do {
    date.setDate(date.getDate() + 1)
  } while (date.getDay() === 0 || date.getDay() === 6)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

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
    mockAuth.role = 'Oktato'
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

  it('shows free times of the day and disables busy fixed slots', async () => {
    const day = nextWeekday()
    vi.mocked(client.get).mockImplementation((url) => {
      if (String(url).endsWith('/availability')) {
        return Promise.resolve(axiosResponse({
          szabad: [{ startTime: new Date(`${day}T09:30:00`).toISOString(), endTime: new Date(`${day}T20:00:00`).toISOString() }],
          foglalt: [{ startTime: new Date(`${day}T08:00:00`).toISOString(), endTime: new Date(`${day}T09:30:00`).toISOString() }],
          karbantartas: [],
        }))
      }
      return Promise.resolve(axiosResponse([{ id: 1, nev: 'Tesztterem', kapacitas: 30, aktivalva: true }]))
    })
    render(<MemoryRouter><NewBookingPage /></MemoryRouter>)
    await screen.findByRole('option', { name: /Tesztterem/ })

    fireEvent.change(screen.getByLabelText('Dátum *'), { target: { value: day } })

    expect(await screen.findByText(/09:30–20:00/)).toBeTruthy()
    const busy = screen.getByRole('option', { name: /1\. sáv .* – foglalt/ }) as HTMLOptionElement
    expect(busy.disabled).toBe(true)
    expect(client.get).toHaveBeenCalledWith('/rooms/1/availability', { params: { datum: day } })
  })

  it('lets an administrator book in the name of an instructor', async () => {
    mockAuth.role = 'Adminisztrator'
    const day = nextWeekday()
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/users') {
        return Promise.resolve(axiosResponse({ items: [{ id: 7, username: 'kiss.anna', nev: 'Kiss Anna', role: 'Oktato' }], totalCount: 1 }))
      }
      if (String(url).endsWith('/availability')) {
        return Promise.resolve(axiosResponse({ szabad: [], foglalt: [], karbantartas: [] }))
      }
      return Promise.resolve(axiosResponse([{ id: 1, nev: 'Tesztterem', kapacitas: 30, aktivalva: true }]))
    })
    vi.mocked(client.post).mockResolvedValue(axiosResponse({}))
    render(<MemoryRouter><NewBookingPage /></MemoryRouter>)

    fireEvent.change(await screen.findByLabelText('Oktató (akinek a nevére a foglalás szól) *'), { target: { value: '7' } })
    await screen.findByRole('option', { name: /Kiss Anna/ })
    fireEvent.change(screen.getByLabelText('Oktató (akinek a nevére a foglalás szól) *'), { target: { value: '7' } })
    fireEvent.change(screen.getByLabelText('Dátum *'), { target: { value: day } })
    fireEvent.click(screen.getByRole('button', { name: /Foglalás/ }))

    await waitFor(() => expect(client.post).toHaveBeenCalledWith('/bookings', expect.objectContaining({
      teremId: 1,
      felhasznaloId: 7,
    })))
  })

  it('warns about weekends before submitting', async () => {
    const date = new Date()
    while (date.getDay() !== 6) date.setDate(date.getDate() + 1)
    const saturday = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
    render(<MemoryRouter><NewBookingPage /></MemoryRouter>)
    await screen.findByRole('option', { name: /Tesztterem/ })

    fireEvent.change(screen.getByLabelText('Dátum *'), { target: { value: saturday } })

    expect(await screen.findByText('Hétvégére nem lehet foglalni.')).toBeTruthy()
  })
})
