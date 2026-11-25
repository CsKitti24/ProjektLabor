// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import { addDays, localDateInputValue, localDateTimeToIso } from '../../utils/dateTime'
import BookingsPage from './BookingsPage'

vi.mock('../../api/client', () => ({
  default: { get: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

const mockAuth = vi.hoisted(() => ({ role: 'Adminisztrator' }))
vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({ user: { userId: 1, username: 'admin', role: mockAuth.role } }),
}))

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return { data, status: 200, statusText: 'OK', headers: new AxiosHeaders(), config: { headers: new AxiosHeaders() } }
}

const day = addDays(localDateInputValue(), 7)
const booking = {
  id: 5,
  teremId: 1,
  teremNev: 'A-101',
  felhasznaloId: 7,
  felhasznaloNev: 'Kiss Anna',
  startTime: localDateTimeToIso(day, '09:30'),
  endTime: localDateTimeToIso(day, '10:15'),
  status: 'Aktiv',
}

describe('BookingsPage', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/bookings') return Promise.resolve(axiosResponse({ items: [booking], totalCount: 1 }))
      if (url === '/users') return Promise.resolve(axiosResponse({ items: [{ id: 7, username: 'kiss.anna', nev: 'Kiss Anna', role: 'Oktato' }], totalCount: 1 }))
      if (String(url).endsWith('/availability')) return Promise.resolve(axiosResponse({ szabad: [], foglalt: [], karbantartas: [] }))
      return Promise.resolve(axiosResponse([{ id: 1, nev: 'A-101' }, { id: 2, nev: 'B-202' }]))
    })
    vi.mocked(client.put).mockResolvedValue(axiosResponse({}))
    vi.mocked(client.delete).mockResolvedValue(axiosResponse({}))
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    mockAuth.role = 'Adminisztrator'
  })

  it('filters on the server by room, status and date range', async () => {
    render(<MemoryRouter><BookingsPage /></MemoryRouter>)
    await screen.findByText('Kiss Anna', { selector: 'td' })

    fireEvent.change(screen.getByLabelText('Terem'), { target: { value: '2' } })
    fireEvent.change(screen.getByLabelText('Állapot'), { target: { value: 'NoShow' } })
    fireEvent.change(screen.getByLabelText('Naptól'), { target: { value: '2026-12-01' } })

    await waitFor(() => expect(client.get).toHaveBeenLastCalledWith('/bookings', {
      params: expect.objectContaining({
        teremId: '2',
        status: 'NoShow',
        tol: localDateTimeToIso('2026-12-01', '00:00'),
        page: 1,
      }),
    }))
  })

  it('cancels a booking after confirmation', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<MemoryRouter><BookingsPage /></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: '#5 foglalás lemondása' }))

    expect(await screen.findByText(/A foglalás lemondva/)).toBeTruthy()
    expect(client.delete).toHaveBeenCalledWith('/bookings/5')
  })

  it('moves a booking to another room and time', async () => {
    render(<MemoryRouter><BookingsPage /></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: '#5 foglalás áthelyezése' }))
    await screen.findByRole('option', { name: 'B-202' })
    fireEvent.change(screen.getByLabelText('Terem', { selector: '#reschedule-room' }), { target: { value: '2' } })
    fireEvent.change(screen.getByLabelText('Kezdés'), { target: { value: '11:00' } })
    fireEvent.change(screen.getByLabelText('Befejezés'), { target: { value: '11:45' } })
    fireEvent.click(screen.getByRole('button', { name: 'Áthelyezés mentése' }))

    await waitFor(() => expect(client.put).toHaveBeenCalledWith('/bookings/5', {
      teremId: 2,
      startTime: localDateTimeToIso(day, '11:00'),
      endTime: localDateTimeToIso(day, '11:45'),
    }))
  })

  it('shows today read-only to porters', async () => {
    mockAuth.role = 'Portas'
    render(<MemoryRouter><BookingsPage /></MemoryRouter>)

    expect(await screen.findByText('Foglalások (mai nap)')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /lemondása/ })).toBeNull()
    await waitFor(() => expect(client.get).toHaveBeenCalledWith('/bookings', {
      params: expect.objectContaining({ tol: localDateTimeToIso(localDateInputValue(), '00:00') }),
    }))
  })
})
