import client from './client'

export interface UserListItem {
  id: number
  username: string
  nev?: string
  email?: string
  role: string
  aktiv?: boolean
  hasPin?: boolean
}

interface UserListResponse {
  items?: UserListItem[]
  totalCount?: number
}

// A backend legfeljebb 100 elemet ad oldalanként; a választólistákhoz minden oldalt lekérünk,
// így 100 fölötti oktatószám mellett sem marad ki senki.
const PAGE_SIZE = 100
const MAX_PAGES = 50

export async function fetchAllUsers(params: { role?: string; aktiv?: boolean } = {}): Promise<UserListItem[]> {
  const result: UserListItem[] = []
  for (let page = 1; page <= MAX_PAGES; page++) {
    const response = await client.get<UserListResponse | UserListItem[]>('/users', {
      params: { ...params, page, pageSize: PAGE_SIZE },
    })
    if (Array.isArray(response.data)) {
      return response.data
    }

    const items = response.data.items ?? []
    result.push(...items)
    const total = response.data.totalCount ?? result.length
    if (items.length < PAGE_SIZE || result.length >= total) {
      break
    }
  }

  return result
}
