// @vitest-environment jsdom

import { useState } from 'react'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ConfirmProvider } from './ConfirmDialog'
import { useConfirm } from './confirmContext'
import DialogKeyboard from './DialogKeyboard'
import ErrorBoundary from './ErrorBoundary'
import NotFoundPage from '../pages/NotFoundPage'

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: null }) }))

function ConfirmProbe() {
  const confirm = useConfirm()
  const [result, setResult] = useState('')
  return (
    <>
      <button type="button" onClick={async () => setResult(String(await confirm({ message: 'Biztosan?', confirmLabel: 'Igen, mehet' })))}>
        Kérdezz
      </button>
      <output>{result}</output>
    </>
  )
}

function Modal({ onClose }: { onClose: () => void }) {
  return (
    <div className="modal-overlay">
      <DialogKeyboard onClose={onClose} />
      <div className="modal" role="dialog" aria-modal="true">
        <label htmlFor="name">Név</label>
        <input id="name" />
        <button type="button">Mentés</button>
      </div>
    </div>
  )
}

function Broken(): never {
  throw new Error('boom')
}

describe('dialogs and fallbacks', () => {
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  it('asks for confirmation in an in-app dialog and resolves with the answer', async () => {
    render(<ConfirmProvider><ConfirmProbe /></ConfirmProvider>)

    fireEvent.click(screen.getByRole('button', { name: 'Kérdezz' }))
    expect(await screen.findByRole('alertdialog')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Igen, mehet' }))
    expect(await screen.findByText('true')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Kérdezz' }))
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(await screen.findByText('false')).toBeTruthy()
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('focuses the first field and closes the modal with Escape', () => {
    const onClose = vi.fn()
    render(<Modal onClose={onClose} />)

    expect(document.activeElement).toBe(screen.getByLabelText('Név'))
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('keeps Tab focus inside the modal', () => {
    render(<Modal onClose={vi.fn()} />)
    const save = screen.getByRole('button', { name: 'Mentés' })
    save.focus()

    fireEvent.keyDown(document, { key: 'Tab' })

    expect(document.activeElement).toBe(screen.getByLabelText('Név'))
  })

  it('shows a readable message instead of a blank page when rendering fails', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    render(<ErrorBoundary><Broken /></ErrorBoundary>)

    expect(screen.getByText('Váratlan hiba történt')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Oldal újratöltése' })).toBeTruthy()
  })

  it('shows a not-found page', () => {
    render(<MemoryRouter><NotFoundPage /></MemoryRouter>)

    expect(screen.getByText('Az oldal nem található')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Vissza a kezdőlapra' })).toBeTruthy()
  })
})
