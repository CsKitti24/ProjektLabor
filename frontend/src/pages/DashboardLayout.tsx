import { useEffect, useState } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import Navbar from '../components/Navbar'
import Sidebar from '../components/Sidebar'
import { useAuth } from '../auth/AuthContext'

export default function DashboardLayout() {
  const { user } = useAuth()
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)

  // Oldalváltáskor a (keskeny képernyőn kicsúszó) menü bezárul.
  useEffect(() => {
    void Promise.resolve().then(() => setMenuOpen(false))
  }, [location.pathname])

  if (!user) return null

  return (
    <div className="dashboard-root">
      <Navbar onMenuToggle={() => setMenuOpen((open) => !open)} isMenuOpen={menuOpen} />
      <div className="dashboard-body">
        <Sidebar role={user.role} isOpen={menuOpen} onNavigate={() => setMenuOpen(false)} />
        {menuOpen && <div className="dashboard-backdrop" onClick={() => setMenuOpen(false)} aria-hidden="true" />}
        <main className="dashboard-main">
          <div className="dashboard-content-container">
            <Outlet />
          </div>
        </main>
      </div>

      <style>{`
        .dashboard-root {
          display: flex;
          flex-direction: column;
          height: 100vh;
          overflow: hidden;
          background-color: var(--color-bg-page);
        }

        .dashboard-body {
          display: flex;
          flex: 1;
          overflow: hidden;
        }

        .dashboard-main {
          flex: 1;
          overflow-y: auto;
          background-color: var(--color-bg-page);
          padding: 28px 32px;
        }

        .dashboard-content-container {
          max-width: 1240px;
          margin: 0 auto;
        }

        .dashboard-backdrop {
          display: none;
        }

        @media (max-width: 900px) {
          .dashboard-main {
            padding: 16px;
          }
          .dashboard-backdrop {
            display: block;
            position: fixed;
            inset: 60px 0 0 0;
            background: rgba(17, 24, 39, 0.35);
            z-index: 90;
          }
        }
      `}</style>
    </div>
  )
}
