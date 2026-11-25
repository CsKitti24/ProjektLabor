// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import MaintenancePage from './MaintenancePage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    put: vi.fn(),
    post: vi.fn(),
    delete: vi.fn(),
  },
}))

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return { data, status: 200, statusText: 'OK', headers: new AxiosHeaders(), config: { headers: new AxiosHeaders() } }
}

const future = new Date(Date.now() + 3 * 24 * 3600 * 1000)
const futureDay = `${future.getFullYear()}-${String(future.getMonth() + 1).padStart(2, '0')}-${String(future.getDate()).padStart(2, '0')}`

describe('MaintenancePage', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/maintenance') {
        return Promise.resolve(axiosResponse([{
          id: 3,
          teremId: 1,
          teremNev: 'A-101',
          felhasznaloNev: 'Admin',
          startTime: new Date(`${futureDay}T08:00:00`).toISOString(),
          endTime: new Date(`${futureDay}T12:00:00`).toISOString(),
          leiras: 'Festés',
        }]))
      }
      return Promise.resolve(axiosResponse([{ id: 1, nev: 'A-101' }, { id: 2, nev: 'B-202' }]))
    })
    vi.mocked(client.post).mockResolvedValue(axiosResponse({}))
    vi.mocked(client.put).mockResolvedValue(axiosResponse({}))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('schedules a maintenance spanning several days', async () => {
    render(<MaintenancePage />)

    fireEvent.click(await screen.findByRole('button', { name: /Új karbantartás/ }))
    fireEvent.change(screen.getByLabelText('Kezdő nap *'), { target: { value: '2030-03-04' } })
    fireEvent.change(screen.getByLabelText('Záró nap *'), { target: { value: '2030-03-06' } })
    fireEvent.change(screen.getByLabelText('Kezdés *'), { target: { value: '18:00' } })
    fireEvent.change(screen.getByLabelText('Befejezés *'), { target: { value: '10:00' } })
    fireEvent.click(screen.getByRole('button', { name: 'Karbantartás rögzítése' }))

    await waitFor(() => expect(client.post).toHaveBeenCalledWith('/maintenance', expect.objectContaining({
      teremId: 1,
      startTime: new Date('2030-03-04T18:00:00').toISOString(),
      endTime: new Date('2030-03-06T10:00:00').toISOString(),
    })))
  })

  it('edits an upcoming maintenance', async () => {
    render(<MaintenancePage />)

    fireEvent.click(await screen.findByRole('button', { name: 'A-101 karbantartás szerkesztése' }))
    expect(screen.getByText('Karbantartás módosítása')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Befejezés *'), { target: { value: '14:00' } })
    fireEvent.click(screen.getByRole('button', { name: 'Módosítás mentése' }))

    await waitFor(() => expect(client.put).toHaveBeenCalledWith('/maintenance/3', expect.objectContaining({
      teremId: 1,
      endTime: new Date(`${futureDay}T14:00:00`).toISOString(),
      leiras: 'Festés',
    })))
    expect(await screen.findByText('A karbantartás módosítva.')).toBeTruthy()
  })

  it('sends conflicting bookings to cancel together with the maintenance', async () => {
    vi.mocked(client.post)
      .mockRejectedValueOnce({
        response: {
          data: {
            message: 'Ütközik',
            conflicts: [{
              foglalasId: 42, felhasznaloId: 5, felhasznaloNev: 'Oktató', teremId: 1, teremNev: 'A-101',
              startTime: new Date(`${futureDay}T09:30:00`).toISOString(),
              endTime: new Date(`${futureDay}T10:15:00`).toISOString(),
              status: 'Aktiv',
            }],
          },
        },
      })
      .mockResolvedValueOnce(axiosResponse({}))
    render(<MaintenancePage />)

    fireEvent.click(await screen.findByRole('button', { name: /Új karbantartás/ }))
    fireEvent.change(screen.getByLabelText('Kezdő nap *'), { target: { value: futureDay } })
    fireEvent.click(screen.getByRole('button', { name: 'Karbantartás rögzítése' }))
    fireEvent.change(await screen.findByLabelText('Teendő'), { target: { value: 'cancel' } })
    fireEvent.click(screen.getByRole('button', { name: /Döntések végrehajtása/ }))

    await waitFor(() => expect(client.post).toHaveBeenLastCalledWith('/maintenance', expect.objectContaining({
      lemondandoFoglalasIds: [42],
    })))
    expect(client.put).not.toHaveBeenCalled()
  })
})
