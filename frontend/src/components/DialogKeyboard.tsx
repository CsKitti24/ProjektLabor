import { useEffect, useRef } from 'react'

const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

/**
 * Felugró ablak billentyűzetkezelése: Esc bezárja, a Tab a modálon belül marad, megnyitáskor az első
 * mezőre kerül a fókusz, bezáráskor pedig visszakerül oda, ahonnan a modált megnyitották.
 * A modál gyökérelemébe (a .modal-overlay-be) kell elhelyezni.
 */
export default function DialogKeyboard({ onClose }: { onClose: () => void }) {
  const anchor = useRef<HTMLSpanElement>(null)
  const onCloseRef = useRef(onClose)

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  useEffect(() => {
    const container = anchor.current?.parentElement
    if (!container) return undefined
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const focusables = () => Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE))
      .filter((element) => !element.closest('[hidden]'))

    if (!container.contains(document.activeElement)) {
      const firstField = focusables().find((element) => element.matches('input, select, textarea'))
      ;(firstField ?? focusables()[0])?.focus()
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onCloseRef.current()
        return
      }
      if (event.key !== 'Tab') return
      const items = focusables()
      if (items.length === 0) return
      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      previouslyFocused?.focus?.()
    }
  }, [])

  return <span ref={anchor} hidden />
}
