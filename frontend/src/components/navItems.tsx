import type { ReactNode } from 'react'
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
  Bell,
  ClipboardList,
  Settings,
} from 'lucide-react'
import type { UserRole } from '../auth/AuthContext'

export interface NavItem {
  to: string
  label: string
  icon: ReactNode
}

export function getNavItems(role: UserRole): NavItem[] {
  switch (role) {
    case 'Oktato':
      return [
        { to: '/my-bookings', label: 'Foglalásaim', icon: <CalendarDays size={17} strokeWidth={1.5} /> },
        { to: '/new-booking', label: 'Új foglalás', icon: <PlusCircle size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
        { to: '/profile', label: 'Megbízhatóságom', icon: <Star size={17} strokeWidth={1.5} /> },
        { to: '/notifications', label: 'Értesítéseim', icon: <Bell size={17} strokeWidth={1.5} /> },
        { to: '/tickets', label: 'Hibajegyek', icon: <FileText size={17} strokeWidth={1.5} /> },
      ]
    case 'Portas':
      return [
        { to: '/key-issue', label: 'Kulcskiadás', icon: <ArrowUpRight size={17} strokeWidth={1.5} /> },
        { to: '/key-return', label: 'Kulcsvisszavétel', icon: <ArrowDownLeft size={17} strokeWidth={1.5} /> },
        { to: '/bookings', label: 'Mai foglalások', icon: <CalendarDays size={17} strokeWidth={1.5} /> },
        { to: '/key-movements', label: 'Kulcsmozgások', icon: <List size={17} strokeWidth={1.5} /> },
        { to: '/active-keys', label: 'Aktív kulcsok', icon: <Key size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
        { to: '/notifications', label: 'Értesítéseim', icon: <Bell size={17} strokeWidth={1.5} /> },
      ]
    case 'Adminisztrator':
      return [
        { to: '/admin', label: 'Áttekintés', icon: <LayoutDashboard size={17} strokeWidth={1.5} /> },
        { to: '/users', label: 'Felhasználók', icon: <Users size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
        { to: '/keys', label: 'Kulcsok', icon: <Key size={17} strokeWidth={1.5} /> },
        { to: '/active-keys', label: 'Aktív kulcsok', icon: <Key size={17} strokeWidth={1.5} /> },
        { to: '/key-movements', label: 'Kulcsmozgások', icon: <List size={17} strokeWidth={1.5} /> },
        { to: '/bookings', label: 'Foglalások', icon: <CalendarDays size={17} strokeWidth={1.5} /> },
        { to: '/maintenance', label: 'Karbantartás', icon: <Wrench size={17} strokeWidth={1.5} /> },
        { to: '/tickets', label: 'Hibajegyek', icon: <FileText size={17} strokeWidth={1.5} /> },
        { to: '/reports', label: 'Riportok', icon: <BarChart2 size={17} strokeWidth={1.5} /> },
        { to: '/audit', label: 'Auditnapló', icon: <ClipboardList size={17} strokeWidth={1.5} /> },
        { to: '/parameters', label: 'Rendszerparaméterek', icon: <Settings size={17} strokeWidth={1.5} /> },
      ]
    case 'UzemeltetesiIgazgato':
      return [
        { to: '/admin', label: 'Áttekintés', icon: <LayoutDashboard size={17} strokeWidth={1.5} /> },
        { to: '/reports', label: 'Riportok', icon: <BarChart2 size={17} strokeWidth={1.5} /> },
        { to: '/active-keys', label: 'Aktív kulcsok', icon: <Key size={17} strokeWidth={1.5} /> },
        { to: '/key-movements', label: 'Kulcsmozgások', icon: <List size={17} strokeWidth={1.5} /> },
        { to: '/bookings', label: 'Foglalások', icon: <CalendarDays size={17} strokeWidth={1.5} /> },
        { to: '/audit', label: 'Auditnapló', icon: <ClipboardList size={17} strokeWidth={1.5} /> },
        { to: '/parameters', label: 'Rendszerparaméterek', icon: <Settings size={17} strokeWidth={1.5} /> },
        { to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> },
        { to: '/tickets', label: 'Hibajegyek', icon: <FileText size={17} strokeWidth={1.5} /> },
        { to: '/profile', label: 'Profil és státusz', icon: <Star size={17} strokeWidth={1.5} /> },
      ]
    default:
      return [{ to: '/rooms', label: 'Termek', icon: <DoorOpen size={17} strokeWidth={1.5} /> }]
  }
}

