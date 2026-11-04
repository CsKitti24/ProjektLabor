import { Outlet } from 'react-router-dom'
import Navbar from '../components/Navbar'
import Sidebar from '../components/Sidebar'
import { useAuth } from '../auth/AuthContext'

export default function DashboardLayout() {
  const { user } = useAuth()

  if (!user) return null

  return (
    <div className="dashboard-root">
      <Navbar />
      <div className="dashboard-body">
        <Sidebar role={user.role} />
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
      `}</style>
    </div>
  )
}
