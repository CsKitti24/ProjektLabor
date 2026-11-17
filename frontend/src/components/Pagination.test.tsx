// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import Pagination from './Pagination'

describe('Pagination', () => {
  afterEach(() => cleanup())

  it('is hidden when everything fits on one page', () => {
    render(<Pagination page={1} pageSize={50} totalCount={50} onPageChange={vi.fn()} />)
    expect(screen.queryByRole('navigation', { name: 'Lapozás' })).toBeNull()
  })

  it('shows the visible range and moves between pages', () => {
    const onPageChange = vi.fn()
    render(<Pagination page={2} pageSize={50} totalCount={120} onPageChange={onPageChange} />)

    expect(screen.getByText('51–100 / 120')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Következő' }))
    fireEvent.click(screen.getByRole('button', { name: 'Előző' }))
    expect(onPageChange).toHaveBeenNthCalledWith(1, 3)
    expect(onPageChange).toHaveBeenNthCalledWith(2, 1)
  })
})
