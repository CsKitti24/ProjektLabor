// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import client from '../api/client'
import { AuthProvider, useAuth } from './AuthContext'

vi.mock('../api/client', () => ({
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

function Probe() {
  const { user, isLoading, login } = useAuth()
  if (isLoading) return <span>betöltés</span>
  return (
    <div>
      <span>{user ? `bejelentkezve: ${user.username} (${user.role})` : 'nincs bejelentkezve'}</span>
      <button type="button" onClick={() => void login('portas', 'Portasjelszo1!')}>belépés</button>
    </div>
  )
}

describe('AuthProvider', () => {
  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
    localStorage.clear()
  })

  it('restores the session from the cookie via /auth/me', async () => {
    vi.mocked(client.get).mockResolvedValue(axiosResponse({ id: 4, username: 'admin', role: 'Adminisztrator' }))
    render(<AuthProvider><Probe /></AuthProvider>)

    expect(await screen.findByText('bejelentkezve: admin (Adminisztrator)')).toBeTruthy()
    expect(client.get).toHaveBeenCalledWith('/auth/me')
  })

  it('logs in without storing the token in localStorage', async () => {
    vi.mocked(client.get).mockRejectedValue({ response: { status: 401 } })
    vi.mocked(client.post).mockResolvedValue(axiosResponse({
      token: 'nem-tarolando-token',
      felhasznalo: { id: 7, username: 'portas', role: 'Portas' },
    }))
    render(<AuthProvider><Probe /></AuthProvider>)

    fireEvent.click(await screen.findByRole('button', { name: 'belépés' }))

    expect(await screen.findByText('bejelentkezve: portas (Portas)')).toBeTruthy()
    expect(JSON.stringify({ ...localStorage })).not.toContain('nem-tarolando-token')
  })
})
