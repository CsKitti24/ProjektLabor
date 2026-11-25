import { createContext, useContext } from 'react'

export interface ConfirmOptions {
  title?: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  danger?: boolean
}

export type ConfirmFunction = (options: ConfirmOptions) => Promise<boolean>

export const ConfirmContext = createContext<ConfirmFunction | null>(null)

// Provider nélkül (pl. önálló komponens-tesztekben) a böngésző saját megerősítő ablakára esik vissza.
const fallbackConfirm: ConfirmFunction = (options) => Promise.resolve(window.confirm(options.message))

export function useConfirm(): ConfirmFunction {
  return useContext(ConfirmContext) ?? fallbackConfirm
}
