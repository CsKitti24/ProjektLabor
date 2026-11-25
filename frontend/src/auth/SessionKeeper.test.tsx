// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import client from '../api/client'
import SessionKeeper from './SessionKeeper'

vi.mock('../api/client', () => ({
  default: { post: vi.fn() },
}))

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return { data, status: 200, statusText: 'OK', headers: new AxiosHeaders(), config: { headers: new AxiosHeaders() } }
}

describe('SessionKeeper', () => {
  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('renews the session on mount and shows nothing while plenty of time is left', async () => {
    const inHours = (h: number) => new Date(Date.now() + h * 3600_000).toISOString()
    vi.mocked(client.post).mockResolvedValue(axiosResponse({ expiresAt: inHours(8), sessionEndsAt: inHours(14) }))
    render(<SessionKeeper onExpired={() => {}} />)

    await vi.waitFor(() => expect(client.post).toHaveBeenCalledWith('/auth/refresh'))
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('warns before the hard session limit and offers no extension', async () => {
    const soon = new Date(Date.now() + 5 * 60_000).toISOString()
    vi.mocked(client.post).mockResolvedValue(axiosResponse({ expiresAt: soon, sessionEndsAt: soon }))
    render(<SessionKeeper onExpired={() => {}} />)

    expect((await screen.findByRole('alert')).textContent).toContain('jelentkezz be újra')
    expect(screen.queryByRole('button', { name: 'Maradok bejelentkezve' })).toBeNull()
  })
})
