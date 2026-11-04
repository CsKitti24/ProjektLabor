import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Key, ArrowRight } from 'lucide-react'
import Navbar from '../components/Navbar'
import LoginModal from '../components/LoginModal'
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

export default function WelcomePage() {
  const [showLoginModal, setShowLoginModal] = useState(false)
  const { user } = useAuth()
  const navigate = useNavigate()

  const handleOpenLogin = () => {
    if (user) {
      navigate(getDashboardPath(user.role))
    } else {
      setShowLoginModal(true)
    }
  }

  return (
    <div className="welcome-wrapper">
      {/* Fejléc: fiók ikonra kattintva is ugyanaz a bejelentkezési felület nyílik meg */}
      <Navbar onLoginClick={handleOpenLogin} />

      <main className="welcome-main">
        <section
          className="welcome-hero"
          style={{
            backgroundImage: `linear-gradient(rgba(17, 24, 39, 0.48), rgba(17, 24, 39, 0.56)), url(/background.png)`,
          }}
        >
          <div className="welcome-hero-content">
            <div className="welcome-icon-wrapper">
              <img src="/logo.png" alt="Logó" className="welcome-logo-img" />
            </div>

            <h1 className="welcome-title">
              Üdvözlünk a Kulcs- és teremkezelő rendszerben!
            </h1>

            <p className="welcome-subtitle">
              A rendszer használatához kérjük, jelentkezz be!
            </p>

            <button
              type="button"
              className="welcome-btn"
              onClick={handleOpenLogin}
              aria-label="Bejelentkezés"
            >
              <span>{user ? 'Tovább a rendszerbe' : 'Bejelentkezés'}</span>
              <ArrowRight size={16} strokeWidth={1.75} />
            </button>
          </div>
        </section>

        <footer className="welcome-footer">
          <div className="welcome-footer-content">
            <span>Kulcs- és teremkezelő rendszer</span>
            <span className="welcome-footer-dot">•</span>
            <span>Intézményi nyilvántartó felület</span>
            <span className="welcome-footer-dot">•</span>
            <span>&copy; {new Date().getFullYear()}</span>
          </div>
        </footer>
      </main>

      {showLoginModal && (
        <LoginModal onClose={() => setShowLoginModal(false)} />
      )}

      <style>{`
        .welcome-wrapper {
          display: flex;
          flex-direction: column;
          min-height: 100vh;
          background-color: var(--color-bg-page);
        }

        .welcome-main {
          display: flex;
          flex-direction: column;
          flex: 1;
        }

        .welcome-hero {
          flex: 1;
          min-height: calc(100vh - 60px - 44px);
          background-size: cover;
          background-position: center center;
          background-repeat: no-repeat;
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 40px 24px;
        }

        .welcome-hero-content {
          text-align: center;
          color: #ffffff;
          max-width: 720px;
          display: flex;
          flex-direction: column;
          align-items: center;
          padding: 20px;
        }

        .welcome-icon-wrapper {
          width: 72px;
          height: 72px;
          border-radius: 50%;
          background: rgba(255, 255, 255, 0.95);
          border: 1px solid rgba(255, 255, 255, 0.4);
          box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
          display: flex;
          align-items: center;
          justify-content: center;
          margin-bottom: 24px;
          padding: 10px;
        }

        .welcome-logo-img {
          width: 44px;
          height: 44px;
          object-fit: contain;
          display: block;
        }

        .welcome-title {
          font-size: 32px;
          font-weight: 600;
          line-height: 1.3;
          margin-bottom: 12px;
          letter-spacing: -0.02em;
          text-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
        }

        .welcome-subtitle {
          font-size: 16px;
          font-weight: 400;
          color: rgba(255, 255, 255, 0.9);
          margin-bottom: 32px;
          letter-spacing: 0.01em;
          text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
        }

        .welcome-btn {
          display: inline-flex;
          align-items: center;
          gap: 10px;
          background-color: var(--color-primary);
          color: #ffffff;
          padding: 12px 32px;
          border-radius: var(--border-radius-sm);
          font-size: 14.5px;
          font-weight: 500;
          letter-spacing: 0.01em;
          cursor: pointer;
          transition: all 0.18s ease;
          border: 1px solid rgba(255, 255, 255, 0.15);
          box-shadow: 0 4px 14px rgba(0, 0, 0, 0.2);
        }

        .welcome-btn:hover {
          background-color: var(--color-primary-hover);
          transform: translateY(-1px);
          box-shadow: 0 6px 20px rgba(0, 0, 0, 0.25);
        }

        .welcome-btn:active {
          transform: translateY(0);
          background-color: var(--color-primary-active);
        }

        .welcome-footer {
          height: 44px;
          background-color: var(--color-white);
          border-top: 1px solid var(--color-gray-200);
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 0 24px;
        }

        .welcome-footer-content {
          font-size: 12px;
          color: var(--color-gray-500);
          display: flex;
          align-items: center;
          gap: 8px;
        }

        .welcome-footer-dot {
          color: var(--color-gray-300);
        }

        @media (max-width: 640px) {
          .welcome-title {
            font-size: 24px;
          }
          .welcome-subtitle {
            font-size: 14px;
          }
          .welcome-btn {
            width: 100%;
            justify-content: center;
          }
        }
      `}</style>
    </div>
  )
}
