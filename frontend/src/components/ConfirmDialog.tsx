import { useCallback, useState, type ReactNode } from 'react'
import { AlertTriangle } from 'lucide-react'
import DialogKeyboard from './DialogKeyboard'
import { ConfirmContext, type ConfirmOptions } from './confirmContext'

interface PendingConfirm extends ConfirmOptions {
  resolve: (confirmed: boolean) => void
}

/**
 * Egységes, a felülethez illeszkedő megerősítő ablak a böngésző saját window.confirm ablaka helyett.
 * Használat: const confirm = useConfirm(); if (!(await confirm({ message: '...' }))) return
 */
export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingConfirm | null>(null)

  const confirm = useCallback((options: ConfirmOptions) =>
    new Promise<boolean>((resolve) => setPending({ ...options, resolve })), [])

  const close = (confirmed: boolean) => {
    pending?.resolve(confirmed)
    setPending(null)
  }

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      {pending && (
        <div className="modal-overlay" onClick={(event) => { if (event.target === event.currentTarget) close(false) }}>
          <DialogKeyboard onClose={() => close(false)} />
          <div className="modal" style={{ maxWidth: 440 }} role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-message">
            <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
              <AlertTriangle size={20} strokeWidth={1.5} color={pending.danger ? 'var(--color-danger)' : 'var(--color-primary)'} style={{ flexShrink: 0, marginTop: 2 }} />
              <div>
                <h2 className="modal-title" id="confirm-title">{pending.title ?? 'Megerősítés'}</h2>
                <p id="confirm-message" style={{ marginTop: 8, whiteSpace: 'pre-line', color: 'var(--color-gray-700)', fontSize: 14 }}>
                  {pending.message}
                </p>
              </div>
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 24 }}>
              <button type="button" className="btn btn-secondary" onClick={() => close(false)}>
                {pending.cancelLabel ?? 'Mégse'}
              </button>
              <button
                type="button"
                className={`btn ${pending.danger ? 'btn-danger' : 'btn-primary'}`}
                onClick={() => close(true)}
              >
                {pending.confirmLabel ?? 'Igen'}
              </button>
            </div>
          </div>
        </div>
      )}
    </ConfirmContext.Provider>
  )
}
