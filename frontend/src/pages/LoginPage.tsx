import { useState, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { Lock, User, AlertCircle, ArrowRight, ArrowLeft } from 'lucide-react'
import { useAuth, type UserRole } from '../auth/AuthContext'

function getDashboardPath(role: UserRole): string {
  switch (role) {
    case 'Oktato':
      return '/my-bookings'
    case 'Portas':
      return '/key-issue'
    case 'Adminisztrator':
    case 'UzemeltetesiIgazgato':
      return '/admin'
    default:
      return '/rooms'
  }
}

const DEMO_ACCOUNTS = [
  { label: 'Oktató', username: 'demo-oktato', pass: 'DemoOktato2026!' },
  { label: 'Portás', username: 'demo-portas', pass: 'DemoPortas2026!' },
  { label: 'Adminisztrátor', username: 'demo-admin', pass: 'DemoAdmin2026!' },
  { label: 'Igazgató', username: 'demo-igazgato', pass: 'DemoIgazgato2026!' },
]

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(false)

  const handleSelectDemo = (u: string, p: string) => {
    setUsername(u)
    setPassword(p)
    setError('')
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!username.trim() || !password) {
      setError('Kérjük, add meg a felhasználónevet és a jelszót!')
      return
    }

    setError('')
    setIsLoading(true)

    try {
      await login(username.trim(), password)
      const role = (localStorage.getItem('user_role') as UserRole) || 'Oktato'
      navigate(getDashboardPath(role))
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      const msg = axiosErr.response?.data?.message || 'Hibás felhasználónév vagy jelszó.'
      setError(msg)
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="login-page-root">
      <div className="login-page-card">
        <Link to="/" className="login-back-link">
          <ArrowLeft size={14} strokeWidth={1.5} />
          <span>Vissza a kezdőlapra</span>
        </Link>

        <div className="login-card-header">
          <div className="login-card-logo">
            <img src="/logo.png" alt="Logó" style={{ width: 26, height: 26, objectFit: 'contain' }} />
          </div>
          <h1 className="login-card-title">Bejelentkezés</h1>
          <p className="login-card-subtitle">Kulcs- és teremkezelő rendszer</p>
        </div>

        {error && (
          <div className="alert alert-error" role="alert">
            <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate>
          <div className="form-group">
            <label className="form-label" htmlFor="page-username">
              Felhasználónév
            </label>
            <div className="login-input-wrapper">
              <User size={15} strokeWidth={1.5} className="login-input-icon" />
              <input
                id="page-username"
                type="text"
                className="form-input login-input-with-icon"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="Felhasználónév"
                autoComplete="username"
                autoFocus
              />
            </div>
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="page-password">
              Jelszó
            </label>
            <div className="login-input-wrapper">
              <Lock size={15} strokeWidth={1.5} className="login-input-icon" />
              <input
                id="page-password"
                type="password"
                className="form-input login-input-with-icon"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••••••"
                autoComplete="current-password"
              />
            </div>
          </div>

          <button
            type="submit"
            className="btn btn-primary login-submit-btn"
            disabled={isLoading}
          >
            {isLoading ? (
              <>
                <span className="spinner" />
                <span>Bejelentkezés…</span>
              </>
            ) : (
              <>
                <span>Bejelentkezés</span>
                <ArrowRight size={15} strokeWidth={1.5} />
              </>
            )}
          </button>
        </form>

        <div className="demo-selector-container">
          <div className="demo-selector-title">Gyorskitöltés a bemutatóhoz:</div>
          <div className="demo-selector-buttons">
            {DEMO_ACCOUNTS.map((acc) => (
              <button
                key={acc.username}
                type="button"
                className="demo-pill-btn"
                onClick={() => handleSelectDemo(acc.username, acc.pass)}
                title={`Kiválasztás: ${acc.username}`}
              >
                {acc.label}
              </button>
            ))}
          </div>
        </div>
      </div>

      <style>{`
        .login-page-root {
          min-height: 100vh;
          background-color: var(--color-bg-page);
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 24px;
        }

        .login-page-card {
          background-color: var(--color-white);
          border: 1px solid var(--color-gray-200);
          border-radius: var(--border-radius);
          padding: 36px 32px;
          width: 100%;
          max-width: 420px;
          box-shadow: 0 4px 16px -2px rgba(0, 0, 0, 0.05);
        }

        .login-back-link {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          font-size: 12.5px;
          color: var(--color-gray-500);
          margin-bottom: 20px;
          transition: color 0.15s ease;
        }

        .login-back-link:hover {
          color: var(--color-primary);
        }

        .login-card-header {
          text-align: center;
          margin-bottom: 24px;
        }

        .login-card-logo {
          width: 44px;
          height: 44px;
          border-radius: var(--border-radius-sm);
          background-color: var(--color-primary-light);
          border: 1px solid var(--color-primary-border);
          display: inline-flex;
          align-items: center;
          justify-content: center;
          margin-bottom: 12px;
        }

        .login-card-title {
          font-size: 20px;
          font-weight: 600;
          color: var(--color-gray-900);
          letter-spacing: -0.01em;
        }

        .login-card-subtitle {
          font-size: 13px;
          color: var(--color-gray-500);
          margin-top: 4px;
        }

        .login-input-wrapper {
          position: relative;
          display: flex;
          align-items: center;
        }

        .login-input-icon {
          position: absolute;
          left: 12px;
          color: var(--color-gray-400);
          pointer-events: none;
        }

        .login-input-with-icon {
          padding-left: 36px;
        }

        .login-submit-btn {
          width: 100%;
          margin-top: 8px;
          padding: 10px 16px;
          font-size: 14px;
        }

        .demo-selector-container {
          margin-top: 24px;
          padding-top: 16px;
          border-top: 1px solid var(--color-gray-200);
        }

        .demo-selector-title {
          font-size: 11.5px;
          font-weight: 500;
          color: var(--color-gray-500);
          margin-bottom: 8px;
          text-align: center;
          text-transform: uppercase;
          letter-spacing: 0.04em;
        }

        .demo-selector-buttons {
          display: grid;
          grid-template-columns: repeat(2, 1fr);
          gap: 6px;
        }

        .demo-pill-btn {
          background-color: var(--color-gray-50);
          border: 1px solid var(--color-gray-200);
          border-radius: var(--border-radius-sm);
          padding: 6px 10px;
          font-size: 12px;
          color: var(--color-gray-700);
          text-align: center;
          transition: all 0.15s ease;
          cursor: pointer;
        }

        .demo-pill-btn:hover {
          background-color: var(--color-primary-light);
          border-color: var(--color-primary-border);
          color: var(--color-primary-hover);
        }
      `}</style>
    </div>
  )
}
