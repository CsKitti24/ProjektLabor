// @vitest-environment jsdom

import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuth } from './auth/AuthContext'
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
      token: 'test-token',
      login: vi.fn(async () => undefined),
      logout: vi.fn(),
      isLoading: false,
    })
  })

  afterEach(() => {
    cleanup()
    window.history.replaceState({}, '', '/')
    vi.clearAllMocks()
  })

  it.each(['/my-bookings', '/new-booking'])(
    'does not expose %s to administrators when its API is instructor-only',
    async (path) => {
      window.history.pushState({}, '', path)
      render(<App />)

      await waitFor(() => {
        expect(window.location.pathname).toBe('/profile')
      })
      expect(screen.getByText('Profile page')).toBeTruthy()
      expect(screen.queryByText('My bookings page')).toBeNull()
      expect(screen.queryByText('New booking page')).toBeNull()
    },
  )

  it.each(['/audit', '/parameters'])(
    'does not expose %s to instructors',
    async (path) => {
      vi.mocked(useAuth).mockReturnValue({
        user: { userId: 2, username: 'teacher', role: 'Oktato' },
        token: 'test-token',
        login: vi.fn(async () => undefined),
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
