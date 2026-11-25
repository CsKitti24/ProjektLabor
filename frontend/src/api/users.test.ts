import { AxiosHeaders, type AxiosResponse } from 'axios'
import { afterEach, describe, expect, it, vi } from 'vitest'
import client from './client'
import { fetchAllUsers } from './users'

vi.mock('./client', () => ({ default: { get: vi.fn() } }))

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return { data, status: 200, statusText: 'OK', headers: new AxiosHeaders(), config: { headers: new AxiosHeaders() } }
}

describe('fetchAllUsers', () => {
  afterEach(() => vi.clearAllMocks())

  it('loads every page so that more than 100 instructors are not cut off', async () => {
    const page = (start: number, count: number) =>
      Array.from({ length: count }, (_, index) => ({ id: start + index, username: `u${start + index}`, role: 'Oktato' }))
    vi.mocked(client.get)
      .mockResolvedValueOnce(axiosResponse({ items: page(1, 100), totalCount: 130 }))
      .mockResolvedValueOnce(axiosResponse({ items: page(101, 30), totalCount: 130 }))

    const users = await fetchAllUsers({ role: 'Oktato', aktiv: true })

    expect(users).toHaveLength(130)
    expect(client.get).toHaveBeenCalledTimes(2)
    expect(client.get).toHaveBeenLastCalledWith('/users', { params: { role: 'Oktato', aktiv: true, page: 2, pageSize: 100 } })
  })
})
