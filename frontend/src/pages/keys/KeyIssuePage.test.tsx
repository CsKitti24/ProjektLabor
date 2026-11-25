// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import KeyIssuePage from './KeyIssuePage'

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

describe('KeyIssuePage', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/keys') {
        return Promise.resolve(axiosResponse([
          { id: 3, szam: 'K-1', tipus: 'Osztalykulcs', teremNev: 'A1', jelenlegiAllapot: 'Szabad', aktivalva: true },
        ]))
      }
      if (url === '/key-movements/recipients') {
        return Promise.resolve(axiosResponse([{ id: 9, nev: 'Teszt Oktató', username: 'teszt' }]))
      }
      return Promise.resolve(axiosResponse([]))
    })
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('tells the porter which booking the issued key was linked to', async () => {
    vi.mocked(client.post).mockResolvedValue(axiosResponse({ foglalasId: 42 }))
    render(<KeyIssuePage />)

    expect(await screen.findByRole('option', { name: /Teszt Oktató/ })).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Jogosult átvevő *'), { target: { value: '9' } })
    fireEvent.change(screen.getByLabelText(/PIN/), { target: { value: '1234' } })
    fireEvent.change(screen.getByLabelText(/Terem állapota/), { target: { value: 'Rendben' } })
    fireEvent.click(screen.getByRole('button', { name: /kiad/i }))

    expect(await screen.findByText(/#42 foglalásához kapcsolódik/)).toBeTruthy()
  })

  it('preselects the instructor with a due booking and marks missing PINs', async () => {
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/keys') {
        return Promise.resolve(axiosResponse([{ id: 3, szam: 'K-1', tipus: 'Osztalykulcs', teremNev: 'A1', jelenlegiAllapot: 'Szabad', aktivalva: true }]))
      }
      if (url === '/key-movements/recipients') {
        return Promise.resolve(axiosResponse([
          { id: 7, nev: 'Foglaló Fanni', username: 'fanni', hasPin: true, esedekesFoglalasKezdete: '2026-11-25T09:30:00Z' },
          { id: 8, nev: 'PIN nélküli Péter', username: 'peter', hasPin: false },
        ]))
      }
      return Promise.resolve(axiosResponse([]))
    })
    render(<KeyIssuePage />)

    await screen.findByRole('option', { name: /Foglaló Fanni/ })
    await waitFor(() => expect((screen.getByLabelText('Jogosult átvevő *') as HTMLSelectElement).value).toBe('7'))
    expect((screen.getByRole('option', { name: /PIN nélküli Péter .*nincs PIN-kódja/ }) as HTMLOptionElement).disabled).toBe(true)
  })

  it('lets a colleague pick up the key for a due booking and then offers the next free key', async () => {
    let keysCall = 0
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/keys') {
        keysCall += 1
        return Promise.resolve(axiosResponse(keysCall === 1
          ? [
              { id: 3, szam: 'K-1', tipus: 'Osztalykulcs', teremNev: 'A1', jelenlegiAllapot: 'Szabad', aktivalva: true },
              { id: 4, szam: 'K-2', tipus: 'Osztalykulcs', teremNev: 'A2', jelenlegiAllapot: 'Szabad', aktivalva: true },
              { id: 5, szam: 'K-3', tipus: 'Osztalykulcs', teremNev: 'Régi', jelenlegiAllapot: 'Szabad', aktivalva: true, teremAktiv: false },
            ]
          : [{ id: 4, szam: 'K-2', tipus: 'Osztalykulcs', teremNev: 'A2', jelenlegiAllapot: 'Szabad', aktivalva: true }]))
      }
      if (url === '/key-movements/recipients') {
        return Promise.resolve(axiosResponse([
          { id: 7, nev: 'Foglaló Fanni', username: 'fanni', hasPin: true, esedekesFoglalasKezdete: '2026-11-25T09:30:00Z' },
          { id: 9, nev: 'Kolléga Kata', username: 'kata', hasPin: true },
        ]))
      }
      if (url === '/key-movements/due-bookings') {
        return Promise.resolve(axiosResponse([
          { foglalasId: 42, felhasznaloId: 7, felhasznaloNev: 'Foglaló Fanni', teremNev: 'A1', startTime: '2026-11-25T09:30:00Z', endTime: '2026-11-25T10:15:00Z' },
        ]))
      }
      return Promise.resolve(axiosResponse([]))
    })
    vi.mocked(client.post).mockResolvedValue(axiosResponse({ foglalasId: 42 }))
    render(<KeyIssuePage />)

    await screen.findByRole('option', { name: /Kolléga Kata/ })
    expect(screen.queryByRole('option', { name: /K-3/ })).toBeNull()
    fireEvent.change(screen.getByLabelText('Jogosult átvevő *'), { target: { value: '9' } })
    fireEvent.change(await screen.findByLabelText('Helyettesítőként veszi fel? (opcionális)'), { target: { value: '42' } })
    fireEvent.change(screen.getByLabelText(/PIN/), { target: { value: '1234' } })
    fireEvent.change(screen.getByLabelText(/Terem állapota/), { target: { value: 'Rendben' } })
    fireEvent.click(screen.getByRole('button', { name: /kiad/i }))

    await waitFor(() => expect(client.post).toHaveBeenCalledWith('/key-movements/issue', expect.objectContaining({
      kulcsId: 3,
      atvevoFelhasznaloId: 9,
      foglalasId: 42,
    })))
    // A kiadott kulcs helyett a következő szabad kulcs lesz kiválasztva (nem a már kiadott marad).
    await waitFor(() => expect((screen.getByLabelText('Kiadandó kulcs *') as HTMLSelectElement).value).toBe('4'))
  })
})
