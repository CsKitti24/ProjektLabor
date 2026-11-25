import { Component, type ErrorInfo, type ReactNode } from 'react'

interface ErrorBoundaryState {
  hasError: boolean
}

/**
 * Váratlan megjelenítési hiba esetén üres fehér oldal helyett érthető üzenetet és újratöltési
 * lehetőséget mutat. A részletek csak a böngésző konzoljába kerülnek.
 */
export default class ErrorBoundary extends Component<{ children: ReactNode }, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Váratlan felületi hiba:', error, info.componentStack)
  }

  render() {
    if (!this.state.hasError) {
      return this.props.children
    }

    return (
      <div role="alert" style={{ maxWidth: 520, margin: '80px auto', padding: 24 }} className="card">
        <h1 style={{ fontSize: 18, fontWeight: 600, marginBottom: 8 }}>Váratlan hiba történt</h1>
        <p style={{ color: 'var(--color-gray-600)', marginBottom: 16 }}>
          Az oldal megjelenítése közben hiba lépett fel. Töltsd újra az oldalt; ha a hiba ismétlődik,
          jelezd az üzemeltetésnek.
        </p>
        <div style={{ display: 'flex', gap: 8 }}>
          <button type="button" className="btn btn-primary" onClick={() => window.location.reload()}>
            Oldal újratöltése
          </button>
          <a className="btn btn-secondary" href="/">Kezdőlap</a>
        </div>
      </div>
    )
  }
}
