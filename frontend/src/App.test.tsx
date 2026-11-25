// @vitest-environment jsdom

import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuth, type UserRole } from './auth/AuthContext'
import App from './App'

vi.mock('./auth/AuthContext', () => ({
  AuthProvider: ({ children }: { children: import('react').ReactNode }) => children,
  useAuth: vi.fn(),
}))

vi.mock('./pages/ProfilePage', () => ({ default: () => 'Profile page' }))
vi.mock('./pages/bookings/MyBookingsPage', () => ({ default: () => 'My bookings page' }))
vi.mock('./pages/bookings/NewBookingPage', () => ({ default: () => 'New booking page' }))
vi.mock('./pages/admin/AuditLogPage', () => ({ default: () => 'Audit log page' }))
vi.mock('./pages/admin/ParametersPage', () => ({ default: () => 'Parameters page' }))

describe('App role-protected booking routes', () => {
  beforeEach(() => {
    vi.mocked(useAuth).mockReturnValue({
      user: { userId: 1, username: 'admin', role: 'Adminisztrator' },
      login: vi.fn(async () => ({ userId: 1, username: 'test-user', role: 'Oktato' as UserRole })),
      logout: vi.fn(),
      isLoading: false,
    })
  })

  afterEach(() => {
    cleanup()
    window.history.replaceState({}, '', '/')
    vi.clearAllMocks()
  })

  it('does not expose /my-bookings to administrators when its API is instructor-only', async () => {
    window.history.pushState({}, '', '/my-bookings')
    render(<App />)

    await waitFor(() => {
      expect(window.location.pathname).toBe('/profile')
    })
    expect(screen.getByText('Profile page')).toBeTruthy()
    expect(screen.queryByText('My bookings page')).toBeNull()
  })

  it('lets administrators open /new-booking to book in the name of an instructor', async () => {
    window.history.pushState({}, '', '/new-booking')
    render(<App />)

    expect(await screen.findByText('New booking page')).toBeTruthy()
    expect(window.location.pathname).toBe('/new-booking')
  })

  it.each(['/audit', '/parameters'])(
    'does not expose %s to instructors',
    async (path) => {
      vi.mocked(useAuth).mockReturnValue({
        user: { userId: 2, username: 'teacher', role: 'Oktato' },
        login: vi.fn(async () => ({ userId: 1, username: 'test-user', role: 'Oktato' as UserRole })),
        logout: vi.fn(),
        isLoading: false,
      })
      window.history.pushState({}, '', path)
      render(<App />)

      await waitFor(() => {
        expect(window.location.pathname).toBe('/profile')
      })
      expect(screen.queryByText('Audit log page')).toBeNull()
      expect(screen.queryByText('Parameters page')).toBeNull()
    },
  )
})
