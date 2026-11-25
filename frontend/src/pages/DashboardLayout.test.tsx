// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import DashboardLayout from './DashboardLayout'

vi.mock('../api/client', () => ({ default: { get: vi.fn(() => Promise.resolve({ data: [], headers: {} })) } }))
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ user: { userId: 1, username: 'admin', nev: 'Admin', role: 'Adminisztrator' }, logout: vi.fn() }),
}))

describe('DashboardLayout on narrow screens', () => {
  afterEach(() => cleanup())

  it('opens the slide-out menu with the menu button and closes it after navigation', () => {
    render(
      <MemoryRouter initialEntries={['/admin']}>
        <Routes>
          <Route element={<DashboardLayout />}>
            <Route path="admin" element={<div>Áttekintés oldal</div>} />
            <Route path="users" element={<div>Felhasználók oldal</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    const sidebar = document.getElementById('app-sidebar')!
    expect(sidebar.className).not.toContain('sidebar--open')

    fireEvent.click(screen.getByLabelText('Menü megnyitása'))
    expect(sidebar.className).toContain('sidebar--open')

    fireEvent.click(screen.getByText('Felhasználók').closest('a')!)
    expect(screen.getByText('Felhasználók oldal')).toBeTruthy()
    expect(sidebar.className).not.toContain('sidebar--open')
  })
})
