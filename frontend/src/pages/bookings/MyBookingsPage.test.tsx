// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import MyBookingsPage from './MyBookingsPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    delete: vi.fn(),
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

const future = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString()
const futureEnd = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000 + 45 * 60 * 1000).toISOString()
const past = new Date(Date.now() - 10 * 60 * 1000).toISOString()
const pastEnd = new Date(Date.now() + 35 * 60 * 1000).toISOString()

describe('MyBookingsPage', () => {
  beforeEach(() => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    vi.mocked(client.get).mockResolvedValue(axiosResponse([
      { id: 1, teremNev: 'Jövőbeli terem', startTime: future, endTime: futureEnd, status: 'Aktiv' },
      { id: 2, teremNev: 'Megkezdett terem', startTime: past, endTime: pastEnd, status: 'Aktiv' },
    ]))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
    vi.restoreAllMocks()
  })

  it('offers cancellation only for bookings that have not started', async () => {
    render(<MemoryRouter><MyBookingsPage /></MemoryRouter>)

    await screen.findByText('Megkezdett terem')
    expect(screen.getAllByRole('button', { name: /Visszavonás/ })).toHaveLength(1)
  })

  it('keeps a cancelled booking in the list and shows the server message on failure', async () => {
    vi.mocked(client.delete)
      .mockResolvedValueOnce(axiosResponse({}))
    render(<MemoryRouter><MyBookingsPage /></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: /Visszavonás/ }))

    expect(await screen.findByText('Jövőbeli terem')).toBeTruthy()
    expect(screen.queryAllByRole('button', { name: /Visszavonás/ })).toHaveLength(0)

    cleanup()
    vi.mocked(client.delete).mockRejectedValueOnce({ response: { data: { message: 'Már megkezdődött foglalás nem mondható le.' } } })
    render(<MemoryRouter><MyBookingsPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: /Visszavonás/ }))
    expect(await screen.findByText('Már megkezdődött foglalás nem mondható le.')).toBeTruthy()
  })
})
