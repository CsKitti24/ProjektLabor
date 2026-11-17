// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import ReportsPage from './ReportsPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
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

describe('ReportsPage reliability tab', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/reports/reliability') {
        return Promise.resolve(axiosResponse({
          tanev: '2026/2027',
          esemenyekSzama: 34,
          erintettOktatokSzama: 1,
          atlagosPontszam: 50,
          esemenytipusok: [],
        }))
      }
      if (url === '/reliability/users') {
        return Promise.resolve(axiosResponse({
          tanev: '2026/2027',
          oktatok: [
            { userId: 10, nev: 'Rossz Pont', username: 'rossz', pontszam: 100, esemenyekSzama: 34, kovetkezmeny: 'Elbeszélgetés és kulcskezelési/biztonsági tréning' },
            { userId: 11, nev: 'Jó Pont', username: 'jo', pontszam: 0, esemenyekSzama: 0, kovetkezmeny: 'Nincs külön intézkedés' },
          ],
        }))
      }
      return Promise.resolve(axiosResponse({}))
    })
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('lists instructors with their reliability score, worst first', async () => {
    render(<ReportsPage />)
    fireEvent.click(await screen.findByRole('button', { name: /Megbízhatóság/ }))

    const table = await screen.findByRole('table', { name: 'Oktatók megbízhatósága' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(within(rows[0]).getByText('Rossz Pont')).toBeTruthy()
    expect(within(rows[0]).getByText('100 / 100')).toBeTruthy()
    expect(within(rows[1]).getByText('Jó Pont')).toBeTruthy()
    expect(client.get).toHaveBeenCalledWith('/reliability/users')
  })
})
