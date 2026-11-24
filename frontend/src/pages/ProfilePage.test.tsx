// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import client from '../api/client'
import ProfilePage from './ProfilePage'

vi.mock('../api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}))

const mockAuth = vi.hoisted(() => ({ role: 'Portas' }))

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { userId: 5, username: 'portas1', nev: 'Portás Pál', email: 'portas1@test.hu', role: mockAuth.role },
    logout: vi.fn(),
  }),
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

function fillPasswordForm(current: string, next: string, confirm: string) {
  fireEvent.change(screen.getByLabelText('Jelenlegi jelszó'), { target: { value: current } })
  fireEvent.change(screen.getByLabelText('Új jelszó (legalább 12 karakter)'), { target: { value: next } })
  fireEvent.change(screen.getByLabelText('Új jelszó megerősítése'), { target: { value: confirm } })
  fireEvent.click(screen.getByRole('button', { name: 'Jelszó módosítása' }))
}

describe('ProfilePage password change', () => {
  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
    mockAuth.role = 'Portas'
  })

  it('rejects a mismatched confirmation without calling the API', async () => {
    render(<MemoryRouter><ProfilePage /></MemoryRouter>)

    fillPasswordForm('RegiJelszo12345', 'UjJelszo1234567', 'MasikJelszo1234')

    expect(await screen.findByText('Az új jelszó és a megerősítés nem egyezik.')).toBeTruthy()
    expect(client.post).not.toHaveBeenCalled()
  })

  it('sends the current and new password and clears the form on success', async () => {
    vi.mocked(client.post).mockResolvedValue(axiosResponse({ message: 'ok' }))
    render(<MemoryRouter><ProfilePage /></MemoryRouter>)

    fillPasswordForm('RegiJelszo12345', 'UjJelszo1234567', 'UjJelszo1234567')

    expect(await screen.findByText('A jelszó sikeresen megváltozott.')).toBeTruthy()
    expect(client.post).toHaveBeenCalledWith('/auth/change-password', {
      jelenlegiJelszo: 'RegiJelszo12345',
      ujJelszo: 'UjJelszo1234567',
    })
    expect((screen.getByLabelText('Jelenlegi jelszó') as HTMLInputElement).value).toBe('')
  })

  it('lets an instructor set their own key pickup PIN after confirming the password', async () => {
    mockAuth.role = 'Oktato'
    vi.mocked(client.get).mockResolvedValue(axiosResponse({ userId: 5, nev: 'Oktató', tanev: '2026/2027', pontszam: 0, kovetkezmeny: 'Nincs' }))
    vi.mocked(client.put).mockResolvedValue(axiosResponse({ message: 'ok' }))
    render(<MemoryRouter><ProfilePage /></MemoryRouter>)

    fireEvent.change(screen.getByLabelText('Jelenlegi jelszó (megerősítéshez)'), { target: { value: 'RegiJelszo12345' } })
    fireEvent.change(screen.getByLabelText('Új PIN-kód (4–8 számjegy)'), { target: { value: '12a4' } })
    fireEvent.click(screen.getByRole('button', { name: 'PIN-kód mentése' }))
    expect(await screen.findByText('A PIN-kód 4–8 számjegyből állhat.')).toBeTruthy()
    expect(client.put).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('Új PIN-kód (4–8 számjegy)'), { target: { value: '4821' } })
    fireEvent.click(screen.getByRole('button', { name: 'PIN-kód mentése' }))
    expect(await screen.findByText('A kulcsátvételi PIN-kód beállítva.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/auth/pin', { jelenlegiJelszo: 'RegiJelszo12345', pin: '4821' })
  })
})
