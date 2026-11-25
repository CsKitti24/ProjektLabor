// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import Navbar from './Navbar'

vi.mock('../api/client', () => ({ default: { get: vi.fn(() => Promise.resolve({ data: [], headers: {} })) } }))

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ user: { userId: 1, username: 'portas', nev: 'Portás', role: 'Portas' }, logout: vi.fn() }),
}))

function Location() {
  const location = useLocation()
  return <div data-testid="location">{location.pathname}{location.search}</div>
}

function renderNavbar() {
  render(
    <MemoryRouter initialEntries={['/key-issue']}>
      <Navbar />
      <Routes>
        <Route path="*" element={<Location />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('Navbar search', () => {
  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('jumps to a menu page whose name matches', () => {
    renderNavbar()

    fireEvent.change(screen.getByLabelText('Keresés'), { target: { value: 'kulcsmozg' } })
    fireEvent.submit(screen.getByRole('search'))

    expect(screen.getByTestId('location').textContent).toBe('/key-movements')
  })

  it('searches rooms when no menu page matches', () => {
    renderNavbar()

    fireEvent.change(screen.getByLabelText('Keresés'), { target: { value: 'B-101' } })
    fireEvent.submit(screen.getByRole('search'))

    expect(screen.getByTestId('location').textContent).toBe('/rooms?q=B-101')
  })
})
