// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import { useAuth, type UserRole } from '../../auth/AuthContext'
import UsersPage from './UsersPage'
import AuditLogPage from './AuditLogPage'
import ParametersPage from './ParametersPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
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
    user: { userId: 1, username: 'admin', role },
    token: 'test-token',
    login: vi.fn(async () => undefined),
    logout: vi.fn(),
    isLoading: false,
  })
}

describe('admin administration pages', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockImplementation((url) => {
      if (url === '/users') return Promise.resolve(axiosResponse({ items: [{ id: 4, username: 'user', nev: 'Teszt User', role: 'Oktato' }] }))
      if (url === '/audit?page=1&pageSize=100') return Promise.resolve(axiosResponse({ items: [{ id: 1, muveletTipus: 'UPDATE', entitasTipus: 'Felhasznalo', entitasId: '4', felhasznaloNev: 'Admin', letrehozasIdeje: '2026-11-10T20:00:00Z' }], totalCount: 1 }))
      return Promise.resolve(axiosResponse([{ kulcs: 'no_show_idokorlat_perc', ertek: '15', leiras: 'Perc' }]))
    })
    vi.mocked(client.put).mockResolvedValue(axiosResponse({}))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('validates and saves a user password through the existing user update endpoint', async () => {
    render(<UsersPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'Módosítás' }))
    const password = screen.getByLabelText('Teszt User új jelszó')
    fireEvent.change(password, { target: { value: 'short' } })
    fireEvent.change(screen.getByLabelText('Teszt User új jelszó megerősítése'), { target: { value: 'short' } })
    fireEvent.click(screen.getByRole('button', { name: 'Jelszó mentése' }))
    expect(await screen.findByText('A jelszónak legalább 8 karakterből kell állnia.')).toBeTruthy()

    fireEvent.change(password, { target: { value: 'BiztonsagosJelszo1!' } })
    fireEvent.change(screen.getByLabelText('Teszt User új jelszó megerősítése'), { target: { value: 'BiztonsagosJelszo1!' } })
    fireEvent.click(screen.getByRole('button', { name: 'Jelszó mentése' }))
    expect(await screen.findByText('A jelszó sikeresen módosítva.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/users/4', { UjJelszo: 'BiztonsagosJelszo1!' })
  })

  it('renders audit events in the protected audit page', async () => {
    render(<AuditLogPage />)
    expect(await screen.findByText('UPDATE')).toBeTruthy()
    expect(screen.getByText('Admin')).toBeTruthy()
    expect(screen.getByText('Felhasznalo #4')).toBeTruthy()
    expect(client.get).toHaveBeenCalledWith('/audit?page=1&pageSize=100')
  })

  it('allows only administrators to edit parameter values', async () => {
    setRole('UzemeltetesiIgazgato')
    render(<ParametersPage />)
    expect(await screen.findByDisplayValue('15')).toBeTruthy()
    expect(screen.getByDisplayValue('15')).toHaveProperty('disabled', true)
    expect(screen.queryByRole('button', { name: 'Mentés' })).toBeNull()

    cleanup()
    setRole('Adminisztrator')
    render(<ParametersPage />)
    expect(await screen.findByRole('button', { name: 'Mentés' })).toBeTruthy()
  })
})
