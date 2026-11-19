// @vitest-environment jsdom

import { AxiosHeaders, type AxiosResponse } from 'axios'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import client from '../../api/client'
import AuditLogPage from './AuditLogPage'

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
  },
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

describe('AuditLogPage', () => {
  beforeEach(() => {
    vi.mocked(client.get).mockResolvedValue(axiosResponse({
      items: [{
        id: 9,
        felhasznaloNev: 'Admin',
        muveletTipus: 'ROLE_CHANGE',
        entitasTipus: 'Felhasznalo',
        entitasId: '4',
        regiErtek: 'Oktato',
        ujErtek: 'Portas',
        indoklas: '1 mesterkulcs-jogosultság megszűnt',
        letrehozasIdeje: '2026-11-19T10:00:00Z',
      }],
      totalCount: 1,
    }))
  })

  afterEach(() => {
    cleanup()
    vi.clearAllMocks()
  })

  it('shows the old and new values and the reason of an entry', async () => {
    render(<AuditLogPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Megnyitás' }))

    expect(screen.getByText('Régi érték')).toBeTruthy()
    expect(screen.getByText('Oktato')).toBeTruthy()
    expect(screen.getByText('Portas')).toBeTruthy()
    expect(screen.getByText('1 mesterkulcs-jogosultság megszűnt')).toBeTruthy()
  })

  it('filters by operation type and resets to the first page', async () => {
    render(<AuditLogPage />)
    await screen.findByText('ROLE_CHANGE')

    fireEvent.change(screen.getByLabelText('Művelet'), { target: { value: 'ACCESS_FORBIDDEN' } })
    fireEvent.click(screen.getByRole('button', { name: /Szűrés/ }))

    await vi.waitFor(() => expect(client.get).toHaveBeenLastCalledWith('/audit', {
      params: { page: 1, pageSize: 50, muveletTipus: 'ACCESS_FORBIDDEN' },
    }))
  })
})
