import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { X, Lock, User, AlertCircle, ArrowRight } from 'lucide-react'
import { useAuth, type UserRole } from '../auth/AuthContext'
import DialogKeyboard from './DialogKeyboard'

interface LoginModalProps {
  onClose: () => void
}

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


export default function LoginModal({ onClose }: LoginModalProps) {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(false)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!username.trim() || !password) {
      setError('Kérjük, add meg a felhasználónevet és a jelszót!')
      return
    }

    setError('')
    setIsLoading(true)

    try {
      const { role } = await login(username.trim(), password)
      onClose()
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
    <div
      className="modal-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <DialogKeyboard onClose={onClose} />
      <div className="modal login-modal-card" role="dialog" aria-modal="true">
        <div className="modal-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <img src="/logo.png" alt="Logó" style={{ width: 28, height: 28, objectFit: 'contain' }} />
            <div>
              <h2 className="modal-title">Bejelentkezés</h2>
              <p className="login-modal-sub">Kulcs- és teremkezelő rendszer</p>
            </div>
          </div>
          <button
            type="button"
            className="modal-close"
            onClick={onClose}
            aria-label="Ablak bezárása"
          >
            <X size={18} strokeWidth={1.5} />
          </button>
        </div>

        {error && (
          <div className="alert alert-error" role="alert">
            <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate>
          <div className="form-group">
            <label className="form-label" htmlFor="login-username">
              Felhasználónév
            </label>
            <div className="login-input-wrapper">
              <User size={15} strokeWidth={1.5} className="login-input-icon" />
              <input
                id="login-username"
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
            <label className="form-label" htmlFor="login-password">
              Jelszó
            </label>
            <div className="login-input-wrapper">
              <Lock size={15} strokeWidth={1.5} className="login-input-icon" />
              <input
                id="login-password"
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

      </div>

      <style>{`
        .login-modal-card {
          max-width: 420px;
          border-radius: var(--border-radius);
          box-shadow: 0 16px 36px -8px rgba(0, 0, 0, 0.16);
        }

        .login-modal-sub {
          font-size: 12.5px;
          color: var(--color-gray-500);
          margin-top: 2px;
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
      `}</style>
    </div>
  )
}
