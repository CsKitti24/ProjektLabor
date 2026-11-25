// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import KeyReturnPage from './KeyReturnPage'

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

describe('KeyReturnPage', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/keys') {
        return Promise.resolve(axiosResponse([
          { id: 7, szam: 'M-1', tipus: 'Mesterkulcs', teremNev: 'A1', jelenlegiAllapot: 'Kiadva', kiadottTeremIsmeretlen: true },
        ]))
      }
      if (url === '/keys/7/rooms') {
        return Promise.resolve(axiosResponse([{ id: 11, nev: 'A1' }, { id: 12, nev: 'B2' }]))
      }
      return Promise.reject(new Error(`unexpected ${String(url)}`))
    })
    vi.mocked(client.post).mockResolvedValue(axiosResponse({}))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('asks for the room when a legacy master key has no recorded issue room', async () => {
    render(<KeyReturnPage />)

    const roomSelect = await screen.findByLabelText('Melyik teremből hozták vissza? *')
    expect(await screen.findByRole('option', { name: 'B2' })).toBeTruthy()

    fireEvent.change(roomSelect, { target: { value: '12' } })
    fireEvent.change(screen.getByLabelText('Az átvevő PIN-kódja *'), { target: { value: '1234' } })
    fireEvent.change(screen.getByLabelText('Terem állapota *'), { target: { value: 'Rendben' } })
    fireEvent.click(screen.getByRole('button', { name: /Visszavétel jóváhagyása/ }))

    expect(await screen.findByText('A kulcs sikeresen visszavételezve a portára!')).toBeTruthy()
    expect(client.post).toHaveBeenCalledWith('/key-movements/return', expect.objectContaining({
      kulcsId: 7,
      teremId: 12,
      pin: '1234',
    }))
  })

  it('records a forced return without PIN only with a reason', async () => {
    render(<KeyReturnPage />)

    await screen.findByRole('option', { name: 'B2' }, { timeout: 5000 })
    fireEvent.change(screen.getByLabelText('Melyik teremből hozták vissza? *'), { target: { value: '12' } })
    fireEvent.click(screen.getByLabelText('Az átvevő nincs jelen (kényszerített visszavétel PIN nélkül)'))
    expect(screen.queryByLabelText('Az átvevő PIN-kódja *')).toBeNull()
    fireEvent.change(screen.getByLabelText('Terem állapota *'), { target: { value: 'Rendben' } })
    fireEvent.click(screen.getByRole('button', { name: /Visszavétel jóváhagyása/ }))
    expect(client.post).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('Indoklás *'), { target: { value: 'Kolléga hozta vissza' } })
    fireEvent.click(screen.getByRole('button', { name: /Visszavétel jóváhagyása/ }))

    expect(await screen.findByText(/kényszerített visszavétele rögzítve/)).toBeTruthy()
    expect(client.post).toHaveBeenCalledWith('/key-movements/force-return', expect.objectContaining({
      kulcsId: 7,
      teremId: 12,
      indoklas: 'Kolléga hozta vissza',
    }))
  })
})
