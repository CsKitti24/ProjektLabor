import { NavLink } from 'react-router-dom'
import {
  Shield,
} from 'lucide-react'
import { useUnreadNotifications } from './useUnreadNotifications'
import { getNavItems } from './navItems'
import type { UserRole } from '../auth/AuthContext'

interface SidebarProps {
  role: UserRole
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

export default function Sidebar({ role }: SidebarProps) {
  const navItems = getNavItems(role)
  const unreadCount = useUnreadNotifications(role === 'Oktato' || role === 'Portas')

  return (
    <aside className="sidebar">
      <div className="sidebar-header">
        <div className="sidebar-role-badge">
          <Shield size={13} strokeWidth={1.5} color="var(--color-primary)" />
          <span className="sidebar-role-text">{getRoleLabel(role)}</span>
        </div>
      </div>

      <nav className="sidebar-nav" aria-label="Oldalsó navigáció">
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/admin'}
            className={({ isActive }) =>
              `sidebar-link${isActive ? ' sidebar-link--active' : ''}`
            }
          >
            <span className="sidebar-link-icon">{item.icon}</span>
            <span className="sidebar-link-label">{item.label}</span>
            {item.to === '/notifications' && unreadCount > 0 && (
              <span
                className="badge badge-primary"
                style={{ marginLeft: 'auto', fontSize: 11, padding: '1px 7px' }}
                aria-label={`${unreadCount} olvasatlan értesítés`}
              >
                {unreadCount > 99 ? '99+' : unreadCount}
              </span>
            )}
          </NavLink>
        ))}
      </nav>

      <style>{`
        .sidebar {
          width: 220px;
          min-width: 220px;
          background-color: var(--color-white);
          border-right: 1px solid var(--color-gray-200);
          display: flex;
          flex-direction: column;
          padding: 16px 0;
          user-select: none;
        }

        .sidebar-header {
          padding: 0 16px 14px 16px;
          margin-bottom: 8px;
          border-bottom: 1px solid var(--color-gray-100);
        }

        .sidebar-role-badge {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          padding: 4px 10px;
          background-color: var(--color-primary-light);
          border: 1px solid var(--color-primary-border);
          border-radius: var(--border-radius-xs);
        }

        .sidebar-role-text {
          font-size: 11.5px;
          font-weight: 600;
          color: var(--color-primary-hover);
          letter-spacing: 0.02em;
          text-transform: uppercase;
        }

        .sidebar-nav {
          display: flex;
          flex-direction: column;
          gap: 2px;
          padding: 0 8px;
        }

        .sidebar-link {
          display: flex;
          align-items: center;
          gap: 10px;
          padding: 8px 12px;
          border-radius: var(--border-radius-sm);
          font-size: 13px;
          font-weight: 500;
          color: var(--color-gray-600);
          text-decoration: none;
          transition: all 0.15s ease;
          border-left: 3px solid transparent;
        }

        .sidebar-link:hover {
          background-color: var(--color-gray-100);
          color: var(--color-gray-900);
        }

        .sidebar-link--active {
          background-color: var(--color-primary-light);
          color: var(--color-primary-hover);
          font-weight: 600;
          border-left-color: var(--color-primary);
        }

        .sidebar-link-icon {
          display: flex;
          align-items: center;
          justify-content: center;
        }

        .sidebar-link-label {
          white-space: nowrap;
        }
      `}</style>
    </aside>
  )
}
