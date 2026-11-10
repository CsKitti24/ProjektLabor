// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import { useAuth, type UserRole } from '../../auth/AuthContext'
import TicketsPage from './TicketsPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: vi.fn(),
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

function setRole(role: UserRole) {
  vi.mocked(useAuth).mockReturnValue({
    user: { userId: 1, username: 'test-user', role },
    token: 'test-token',
    login: vi.fn(async () => undefined),
    logout: vi.fn(),
    isLoading: false,
  })
}

describe('TicketsPage role access', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockResolvedValue(axiosResponse([]))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('lets instructors create tickets', () => {
    setRole('Oktato')
    render(<TicketsPage />)

    expect(screen.getByRole('button', { name: 'Új hibajegy' })).toBeTruthy()
  })

  it('does not offer ticket creation to the operations director', () => {
    setRole('UzemeltetesiIgazgato')
    render(<TicketsPage />)

    expect(screen.queryByRole('button', { name: 'Új hibajegy' })).toBeNull()
  })
})
