// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import client from '../api/client'
import { localDateTimeToIso } from '../utils/dateTime'
import KeyMovementsPage from './keys/KeyMovementsPage'
import ParametersPage from './admin/ParametersPage'

vi.mock('../api/client', () => ({ default: { get: vi.fn(), put: vi.fn() } }))
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ user: { userId: 1, username: 'admin', role: 'Adminisztrator' } }),
}))

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return { data, status: 200, statusText: 'OK', headers: new AxiosHeaders(), config: { headers: new AxiosHeaders() } }
}

describe('filters and reasons', () => {
  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('filters key movements by key, type and day on the server', async () => {
    vi.mocked(client.get).mockImplementation((url) => Promise.resolve(axiosResponse(url === '/keys'
      ? [{ id: 4, szam: 'K-104', teremNev: 'A-104' }]
      : { items: [], totalCount: 0 })))
    render(<KeyMovementsPage />)

    fireEvent.change(await screen.findByLabelText('Kulcs'), { target: { value: '4' } })
    await screen.findByRole('option', { name: 'K-104 – A-104' })
    fireEvent.change(screen.getByLabelText('Kulcs'), { target: { value: '4' } })
    fireEvent.change(screen.getByLabelText('Mozgás'), { target: { value: 'Kiadás' } })
    fireEvent.change(screen.getByLabelText('Naptól'), { target: { value: '2026-11-02' } })

    await waitFor(() => expect(client.get).toHaveBeenLastCalledWith('/key-movements', {
      params: expect.objectContaining({
        kulcsId: '4',
        mozgasTipus: 'Kiadás',
        tol: localDateTimeToIso('2026-11-02', '00:00'),
        page: 1,
      }),
    }))
  })

  it('sends the reason of a parameter change to the audit log', async () => {
    vi.mocked(client.get).mockResolvedValue(axiosResponse([{ kulcs: 'no_show_idokorlat_perc', ertek: '15', leiras: 'Türelmi idő' }]))
    vi.mocked(client.put).mockResolvedValue(axiosResponse({ kulcs: 'no_show_idokorlat_perc', ertek: '20', leiras: 'Türelmi idő' }))
    render(<ParametersPage />)

    fireEvent.change(await screen.findByLabelText('no_show_idokorlat_perc értéke'), { target: { value: '20' } })
    fireEvent.change(screen.getByLabelText('no_show_idokorlat_perc módosításának oka'), { target: { value: 'Új órarend' } })
    fireEvent.click(screen.getByRole('button', { name: 'Mentés' }))

    await waitFor(() => expect(client.put).toHaveBeenCalledWith('/parameters/no_show_idokorlat_perc', {
      ertek: '20',
      indoklas: 'Új órarend',
    }))
  })
})
