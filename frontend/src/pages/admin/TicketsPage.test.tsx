// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import { useAuth, type UserRole } from '../../auth/AuthContext'
import TicketsPage from './TicketsPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
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
    login: vi.fn(async () => ({ userId: 1, username: 'test-user', role: 'Oktato' as UserRole })),
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

  it('lets administrators move a ticket only to an allowed next status', async () => {
    setRole('Adminisztrator')
    vi.mocked(client.get).mockImplementation((url) =>
      Promise.resolve(axiosResponse(url === '/tickets'
        ? { items: [{ id: 7, teremNev: 'A1', kategoria: 'Technikai hiba', leiras: 'Projektor', statusz: 'Megoldva' }], totalCount: 1 }
        : [])))
    vi.mocked(client.put).mockResolvedValue(axiosResponse({}))
    render(<TicketsPage />)

    const select = await screen.findByLabelText('#7 hibajegy állapotának módosítása')
    expect(screen.queryByRole('option', { name: 'Folyamatban' })).toBeNull()
    fireEvent.change(select, { target: { value: 'Zarva' } })

    expect(await screen.findByText('A(z) #7 hibajegy állapota: Lezárva.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/tickets/7/status', { statusz: 'Zarva' })
  })
})
