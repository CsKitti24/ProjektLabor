import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Search, User as UserIcon, Key, LogOut } from 'lucide-react'
import { useAuth, type UserRole } from '../auth/AuthContext'
import LoginModal from './LoginModal'

interface NavbarProps {
  onLoginClick?: () => void
}

function getRoleLabel(role: UserRole): string {
  switch (role) {
    case 'Oktato':
      return 'Oktató'
    case 'Portas':
      return 'Portás'
    case 'Adminisztrator':
      return 'Adminisztrátor'
    case 'UzemeltetesiIgazgato':
      return 'Üzemeltetési igazgató'
    default:
      return role
  }
}

export default function Navbar({ onLoginClick }: NavbarProps) {
  const { user, logout } = useAuth()
  const [showLoginModal, setShowLoginModal] = useState(false)
  const navigate = useNavigate()

  const handleProfileClick = () => {
    if (!user) {
      if (onLoginClick) {
        onLoginClick()
      } else {
        setShowLoginModal(true)
      }
    } else {
      navigate('/profile')
    }
  }

  const handleLogout = () => {
    logout()
    navigate('/')
  }

  return (
    <>
      <header className="navbar">
        {/* Bal oldal: Rendszer neve és logó */}
        <Link to={user ? '#' : '/'} className="navbar-brand-container">
          <div className="navbar-logo-badge">
            <img src="/logo.png" alt="Logó" className="navbar-logo-img" />
          </div>
          <span className="navbar-title">Kulcs- és teremkezelő rendszer</span>
        </Link>

        {/* Középen: Inaktív keresősáv */}
        <div className="navbar-center">
          <div className="navbar-search" title="A keresés jelenleg nem aktív">
            <Search size={15} strokeWidth={1.5} color="var(--color-gray-400)" />
            <input
              type="text"
              placeholder="Keresés a rendszerben…"
              disabled
              aria-label="Keresés"
            />
          </div>
        </div>

        {/* Jobb oldal: Fiók / Profil vagy felhasználó kezelése */}
        <div className="navbar-right">
          {user ? (
            <div className="navbar-user-actions">
              <Link to="/profile" className="navbar-user-info" title="Profil megtekintése">
                <div className="navbar-avatar-icon">
                  <UserIcon size={15} strokeWidth={1.5} color="var(--color-gray-700)" />
                </div>
                <div className="navbar-user-text">
                  <span className="navbar-user-name">{user.nev || user.username}</span>
                  <span className="navbar-user-role">{getRoleLabel(user.role)}</span>
                </div>
              </Link>
              <button
                type="button"
                className="btn btn-ghost btn-sm navbar-logout-btn"
                onClick={handleLogout}
                title="Kijelentkezés"
              >
                <LogOut size={15} strokeWidth={1.5} />
                <span>Kilépés</span>
              </button>
            </div>
          ) : (
            <button
              type="button"
              className="navbar-account-btn"
              onClick={handleProfileClick}
              title="Bejelentkezés"
              aria-label="Bejelentkezés"
            >
              <div className="navbar-account-icon">
                <UserIcon size={16} strokeWidth={1.5} />
              </div>
              <span className="navbar-account-text">Fiók</span>
            </button>
          )}
        </div>
      </header>

      {showLoginModal && (
        <LoginModal onClose={() => setShowLoginModal(false)} />
      )}

      <style>{`
        .navbar {
          height: 60px;
          background-color: var(--color-white);
          border-bottom: 1px solid var(--color-gray-200);
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 0 28px;
          position: sticky;
          top: 0;
          z-index: 90;
          box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.02);
        }

        .navbar-brand-container {
          display: flex;
          align-items: center;
          gap: 12px;
          min-width: 270px;
          text-decoration: none;
          color: inherit;
        }

        .navbar-logo-badge {
          width: 32px;
          height: 32px;
          border-radius: var(--border-radius-sm);
          background-color: var(--color-white);
          border: 1px solid var(--color-gray-200);
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 4px;
        }

        .navbar-logo-img {
          width: 100%;
          height: 100%;
          object-fit: contain;
          display: block;
        }

        .navbar-title {
          font-size: 14.5px;
          font-weight: 600;
          color: var(--color-gray-900);
          letter-spacing: -0.01em;
          white-space: nowrap;
        }

        .navbar-center {
          flex: 1;
          display: flex;
          justify-content: center;
          padding: 0 16px;
        }

        .navbar-search {
          display: flex;
          align-items: center;
          gap: 9px;
          padding: 6px 14px;
          background-color: var(--color-gray-50);
          border: 1px solid var(--color-gray-200);
          border-radius: var(--border-radius-sm);
          width: 100%;
          max-width: 340px;
          cursor: not-allowed;
          transition: border-color 0.15s ease;
        }

        .navbar-search input {
          border: none;
          background: transparent;
          outline: none;
          font-size: 13px;
          color: var(--color-gray-500);
          width: 100%;
          cursor: not-allowed;
        }

        .navbar-search input::placeholder {
          color: var(--color-gray-400);
        }

        .navbar-right {
          display: flex;
          align-items: center;
          justify-content: flex-end;
          min-width: 270px;
        }

        .navbar-account-btn {
          display: inline-flex;
          align-items: center;
          gap: 8px;
          padding: 6px 14px;
          border-radius: var(--border-radius-sm);
          border: 1px solid var(--color-gray-200);
          background-color: var(--color-white);
          color: var(--color-gray-700);
          font-size: 13px;
          font-weight: 500;
          transition: all 0.15s ease;
          cursor: pointer;
        }

        .navbar-account-btn:hover {
          background-color: var(--color-primary-light);
          border-color: var(--color-primary-border);
          color: var(--color-primary-hover);
        }

        .navbar-account-icon {
          display: flex;
          align-items: center;
          justify-content: center;
        }

        .navbar-account-text {
          font-size: 13px;
        }

        .navbar-user-actions {
          display: flex;
          align-items: center;
          gap: 14px;
        }

        .navbar-user-info {
          display: flex;
          align-items: center;
          gap: 10px;
          text-decoration: none;
          padding: 4px 8px;
          border-radius: var(--border-radius-sm);
          transition: background-color 0.15s ease;
        }

        .navbar-user-info:hover {
          background-color: var(--color-gray-100);
        }

        .navbar-avatar-icon {
          width: 30px;
          height: 30px;
          border-radius: 50%;
          background-color: var(--color-gray-100);
          border: 1px solid var(--color-gray-200);
          display: flex;
          align-items: center;
          justify-content: center;
        }

        .navbar-user-text {
          display: flex;
          flex-direction: column;
          line-height: 1.25;
          text-align: left;
        }

        .navbar-user-name {
          font-size: 13px;
          font-weight: 600;
          color: var(--color-gray-900);
        }

        .navbar-user-role {
          font-size: 11px;
          color: var(--color-gray-500);
        }

        .navbar-logout-btn {
          color: var(--color-gray-500);
          font-size: 12.5px;
        }

        .navbar-logout-btn:hover {
          color: var(--color-danger);
          background-color: var(--color-danger-bg);
        }
      `}</style>
    </>
  )
}
