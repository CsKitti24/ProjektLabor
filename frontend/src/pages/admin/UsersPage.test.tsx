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
    fireEvent.change(screen.getByLabelText('Aktív Oktató szerepköre'), { target: { value: 'Portas' } })
    fireEvent.click(screen.getByRole('button', { name: 'Szerepkör mentése' }))

    expect(await screen.findByText('A szerepkör sikeresen frissítve.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/users/4/role', { ujRole: 'Portas' })
  })

  it('edits the name and e-mail address of a user', async () => {
    render(<UsersPage />)

    fireEvent.click((await screen.findAllByRole('button', { name: 'Módosítás' }))[0])
    fireEvent.change(screen.getByLabelText('Aktív Oktató neve'), { target: { value: 'Új Név' } })
    fireEvent.change(screen.getByLabelText('Aktív Oktató e-mail címe'), { target: { value: 'uj@iskola.hu' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adatok mentése' }))

    expect(await screen.findByText('A felhasználó adatai frissítve.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/users/4', { nev: 'Új Név', email: 'uj@iskola.hu' })
  })

  it('filters by role and status and searches after typing', async () => {
    render(<UsersPage />)
    await screen.findByText('Aktív Oktató')

    fireEvent.change(screen.getByLabelText('Szűrés szerepkör szerint'), { target: { value: 'Portas' } })
    fireEvent.change(screen.getByLabelText('Szűrés állapot szerint'), { target: { value: 'inactive' } })
    fireEvent.change(screen.getByLabelText('Keresés név, felhasználónév vagy e-mail szerint'), { target: { value: 'kovacs' } })

    await vi.waitFor(() => expect(client.get).toHaveBeenLastCalledWith('/users', {
      params: { page: 1, pageSize: 50, kereses: 'kovacs', role: 'Portas', aktiv: false },
    }))
  })

  it('creates a new instructor with an initial PIN', async () => {
    vi.mocked(client.post).mockResolvedValue(axiosResponse({ id: 9 }))
    render(<UsersPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Új felhasználó' }))
    fireEvent.change(screen.getByLabelText('Teljes név *'), { target: { value: 'Kiss Anna' } })
    fireEvent.change(screen.getByLabelText('Felhasználónév *'), { target: { value: 'kiss.anna' } })
    fireEvent.change(screen.getByLabelText('E-mail cím *'), { target: { value: 'kiss.anna@iskola.hu' } })
    fireEvent.change(screen.getByLabelText('Kezdő jelszó (legalább 12 karakter) *'), { target: { value: 'Kezdo-jelszo-2026' } })
    fireEvent.change(screen.getByLabelText('Jelszó megerősítése *'), { target: { value: 'Kezdo-jelszo-2026' } })
    fireEvent.change(screen.getByLabelText('Kulcsátvételi PIN (4–8 számjegy, később is megadható)'), { target: { value: '4821' } })
    fireEvent.click(screen.getByRole('button', { name: 'Létrehozás' }))

    expect(await screen.findByText('Kiss Anna felhasználó létrehozva.')).toBeTruthy()
    expect(client.post).toHaveBeenCalledWith('/users', {
      nev: 'Kiss Anna',
      username: 'kiss.anna',
      email: 'kiss.anna@iskola.hu',
      role: 'Oktato',
      jelszo: 'Kezdo-jelszo-2026',
    })
    expect(client.put).toHaveBeenCalledWith('/users/9/pin', { pin: '4821' })
  })

  it('rejects an invalid username before calling the API', async () => {
    render(<UsersPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Új felhasználó' }))
    fireEvent.change(screen.getByLabelText('Teljes név *'), { target: { value: 'Kiss Anna' } })
    fireEvent.change(screen.getByLabelText('Felhasználónév *'), { target: { value: 'kiss anna' } })
    fireEvent.change(screen.getByLabelText('E-mail cím *'), { target: { value: 'kiss.anna@iskola.hu' } })
    fireEvent.click(screen.getByRole('button', { name: 'Létrehozás' }))

    expect(await screen.findByText(/A felhasználónév 3–50 karakter/)).toBeTruthy()
    expect(client.post).not.toHaveBeenCalled()
  })
})
