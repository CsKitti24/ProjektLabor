// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import UsersPage from './UsersPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    put: vi.fn(),
    post: vi.fn(),
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

describe('UsersPage', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockResolvedValue(axiosResponse({
      items: [
        { id: 4, username: 'aktiv', nev: 'Aktív Oktató', role: 'Oktato', aktiv: true },
        { id: 5, username: 'inaktiv', nev: 'Inaktív Oktató', role: 'Oktato', aktiv: false },
      ],
    }))
    vi.mocked(client.put).mockResolvedValue(axiosResponse({}))
    vi.mocked(client.post).mockResolvedValue(axiosResponse({}))
    vi.mocked(client.delete).mockResolvedValue(axiosResponse({}))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('marks inactive users and reactivates them', async () => {
    render(<UsersPage />)

    expect(await screen.findByText('Inaktív')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Újraaktiválás' }))

    expect(await screen.findByText('A felhasználó újra aktív.')).toBeTruthy()
    expect(client.post).toHaveBeenCalledWith('/users/5/reactivate')
    expect(screen.queryByText('Inaktív')).toBeNull()
  })

  it('deactivates a user only after confirmation', async () => {
    render(<UsersPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Inaktiválás' }))
    expect(client.delete).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Igen, inaktiválás' }))

    expect(await screen.findByText(/A felhasználó inaktiválva lett/)).toBeTruthy()
    expect(client.delete).toHaveBeenCalledWith('/users/4')
    expect(screen.getAllByText('Inaktív')).toHaveLength(2)
  })

  it('sends the role change in the field the backend expects', async () => {
    render(<UsersPage />)

    fireEvent.click((await screen.findAllByRole('button', { name: 'Módosítás' }))[0])
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Portas' } })
    fireEvent.click(screen.getByRole('button', { name: 'Mentés' }))

    expect(await screen.findByText('A szerepkör sikeresen frissítve.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/users/4/role', { ujRole: 'Portas' })
  })
})
