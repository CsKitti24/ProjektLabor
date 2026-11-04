import { NavLink } from 'react-router-dom'
import {
  LayoutDashboard,
  Users,
  DoorOpen,
  Key,
  CalendarDays,
  PlusCircle,
  Wrench,
  FileText,
  BarChart2,
  Star,
  ArrowDownLeft,
  ArrowUpRight,
  List,
  Shield,
} from 'lucide-react'
import type { UserRole } from '../auth/AuthContext'

interface SidebarProps {
  role: UserRole
}

interface NavItem {
  to: string
  label: string
  icon: React.ReactNode
}

function getNavItems(role: UserRole): NavItem[] {
  switch (role) {
    case 'Oktato':
      return [
        { to: '/my-bookings', label: 'Foglalásaim', icon: <CalendarDays size={17} strokeWidth={1.5} /> },
        { to: '/new-booking', label: 'Új foglalás', icon: <PlusCircle size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
        { to: '/profile', label: 'Megbízhatóságom', icon: <Star size={17} strokeWidth={1.5} /> },
        { to: '/tickets', label: 'Hibajegyek', icon: <FileText size={17} strokeWidth={1.5} /> },
      ]
    case 'Portas':
      return [
        { to: '/key-issue', label: 'Kulcskiadás', icon: <ArrowUpRight size={17} strokeWidth={1.5} /> },
        { to: '/key-return', label: 'Kulcsvisszavétel', icon: <ArrowDownLeft size={17} strokeWidth={1.5} /> },
        { to: '/key-movements', label: 'Kulcsmozgások', icon: <List size={17} strokeWidth={1.5} /> },
        { to: '/active-keys', label: 'Aktív kulcsok', icon: <Key size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
      ]
    case 'Adminisztrator':
      return [
        { to: '/admin', label: 'Áttekintés', icon: <LayoutDashboard size={17} strokeWidth={1.5} /> },
        { to: '/users', label: 'Felhasználók', icon: <Users size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
        { to: '/keys', label: 'Kulcsok', icon: <Key size={17} strokeWidth={1.5} /> },
        { to: '/bookings', label: 'Foglalások', icon: <CalendarDays size={17} strokeWidth={1.5} /> },
        { to: '/maintenance', label: 'Karbantartás', icon: <Wrench size={17} strokeWidth={1.5} /> },
        { to: '/tickets', label: 'Hibajegyek', icon: <FileText size={17} strokeWidth={1.5} /> },
        { to: '/reports', label: 'Riportok', icon: <BarChart2 size={17} strokeWidth={1.5} /> },
      ]
    case 'UzemeltetesiIgazgato':
      return [
        { to: '/admin', label: 'Áttekintés', icon: <LayoutDashboard size={17} strokeWidth={1.5} /> },
        { to: '/reports', label: 'Riportok', icon: <BarChart2 size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
        { to: '/tickets', label: 'Hibajegyek', icon: <FileText size={17} strokeWidth={1.5} /> },
        { to: '/profile', label: 'Profil és státusz', icon: <Star size={17} strokeWidth={1.5} /> },
      ]
    default:
      return [{ to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> }]
  }
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
