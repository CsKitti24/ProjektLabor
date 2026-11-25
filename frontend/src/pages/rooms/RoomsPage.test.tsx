// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import { MemoryRouter } from 'react-router-dom'
import RoomsPage from './RoomsPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    put: vi.fn(),
    post: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({ user: { userId: 1, username: 'admin', role: 'Adminisztrator' } }),
}))

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return { data, status: 200, statusText: 'OK', headers: new AxiosHeaders(), config: { headers: new AxiosHeaders() } }
}

describe('RoomsPage (admin)', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockResolvedValue(axiosResponse([
      { id: 1, nev: 'A-101', kapacitas: 30, aktivalva: true },
      { id: 2, nev: 'B-202', kapacitas: 20, aktivalva: false },
    ]))
    vi.mocked(client.put).mockResolvedValue(axiosResponse({}))
    vi.mocked(client.delete).mockResolvedValue(axiosResponse({}))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('reactivates an inactive room', async () => {
    render(<MemoryRouter><RoomsPage /></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: 'Újraaktiválás' }))

    expect(await screen.findByText('A(z) B-202 terem újra aktív és foglalható.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/rooms/2', { aktivalva: true })
    expect(screen.queryByText('Inaktív')).toBeNull()
  })

  it('edits the data of an existing room', async () => {
    render(<MemoryRouter><RoomsPage /></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: 'A-101 szerkesztése' }))
    expect(screen.getByText('Terem szerkesztése')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Terem neve / azonosítója *'), { target: { value: 'A-101/B' } })
    fireEvent.change(screen.getByLabelText('Befogadóképesség (fő) *'), { target: { value: '35' } })
    fireEvent.click(screen.getByRole('button', { name: 'Terem mentése' }))

    expect(await screen.findByText('A terem adatai frissítve.')).toBeTruthy()
    expect(client.put).toHaveBeenCalledWith('/rooms/1', {
      nev: 'A-101/B',
      kapacitas: 35,
      leiras: '',
      felszereltseg: '',
    })
  })

  it('keeps a deactivated room in the list as inactive', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<MemoryRouter><RoomsPage /></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: 'Deaktiválás' }))

    expect(await screen.findByText('A terem deaktiválva.')).toBeTruthy()
    expect(client.delete).toHaveBeenCalledWith('/rooms/1')
    expect(screen.getAllByText('Inaktív')).toHaveLength(2)
  })

  it('starts with the search term coming from the navbar', async () => {
    render(<MemoryRouter initialEntries={['/rooms?q=B-2']}><RoomsPage /></MemoryRouter>)

    expect(await screen.findByText('B-202')).toBeTruthy()
    expect(screen.queryByText('A-101')).toBeNull()
  })
})
