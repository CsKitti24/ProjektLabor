// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
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
    fireEvent.change(screen.getByLabelText(/PIN/), { target: { value: '1234' } })
    fireEvent.change(screen.getByLabelText(/Terem állapota/), { target: { value: 'Rendben' } })
    fireEvent.click(screen.getByRole('button', { name: /kiad/i }))

    expect(await screen.findByText(/#42 foglalásához kapcsolódik/)).toBeTruthy()
  })
})
