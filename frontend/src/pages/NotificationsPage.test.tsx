// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../api/client'
import NotificationsPage from './NotificationsPage'

vi.mock('../api/client', () => ({
  default: {
    get: vi.fn(),
    patch: vi.fn(),
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

describe('NotificationsPage', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockResolvedValue(axiosResponse([
      {
        id: 17,
        foglalasId: null,
        ertesitesTipus: 'EveningKeyReturn',
        idoPont: '2026-04-04T20:00:00Z',
        allapot: 'Kesz',
        createdAt: '2026-04-04T20:00:00Z',
        foglalasKezdete: null,
        foglalasVege: null,
        olvasva: false,
        kulcsSzam: 'KEY-PORTER-1',
        teremNev: '101-es terem',
        hatarido: '2026-04-04T20:00:00Z',
      },
    ]))
    vi.mocked(client.patch).mockResolvedValue(axiosResponse(undefined))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('shows key, room, deadline, and marks the notification as read', async () => {
    render(<NotificationsPage />)

    expect(await screen.findByText('Esti kulcsvisszavétel szükséges')).toBeTruthy()
    expect(screen.getByText('KEY-PORTER-1')).toBeTruthy()
    expect(screen.getByText('101-es terem')).toBeTruthy()
    expect(screen.getByText('2026. 04. 04. 22:00', { exact: true })).toBeTruthy()
    expect(screen.getByText('Olvasatlan')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Megjelölés olvasottként' }))

    expect(await screen.findByText('Olvasott')).toBeTruthy()
    expect(client.patch).toHaveBeenCalledWith('/notifications/17/read')
    expect(screen.queryByRole('button', { name: 'Megjelölés olvasottként' })).toBeNull()
  })
})
