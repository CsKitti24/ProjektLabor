import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import ProtectedRoute from './components/ProtectedRoute'
import ErrorBoundary from './components/ErrorBoundary'
import { ConfirmProvider } from './components/ConfirmDialog'
import NotFoundPage from './pages/NotFoundPage'

import WelcomePage from './pages/WelcomePage'
import LoginPage from './pages/LoginPage'
import DashboardLayout from './pages/DashboardLayout'
import RoomsPage from './pages/rooms/RoomsPage'
import MyBookingsPage from './pages/bookings/MyBookingsPage'
import NewBookingPage from './pages/bookings/NewBookingPage'
import KeyIssuePage from './pages/keys/KeyIssuePage'
import KeyReturnPage from './pages/keys/KeyReturnPage'
import KeyMovementsPage from './pages/keys/KeyMovementsPage'
import ActiveKeysPage from './pages/keys/ActiveKeysPage'
import AdminDashboard from './pages/admin/AdminDashboard'
import UsersPage from './pages/admin/UsersPage'
import BookingsPage from './pages/admin/BookingsPage'
import KeysPage from './pages/admin/KeysPage'
import MaintenancePage from './pages/admin/MaintenancePage'
import TicketsPage from './pages/admin/TicketsPage'
import ReportsPage from './pages/admin/ReportsPage'
import ProfilePage from './pages/ProfilePage'
import NotificationsPage from './pages/NotificationsPage'
import AuditLogPage from './pages/admin/AuditLogPage'
import ParametersPage from './pages/admin/ParametersPage'

export default function App() {
  return (
    <BrowserRouter>
      <ErrorBoundary>
      <AuthProvider>
      <ConfirmProvider>
        <Routes>
          {/* Public */}
          <Route path="/" element={<WelcomePage />} />
          <Route path="/login" element={<LoginPage />} />

          {/* Protected dashboard routes */}
          <Route
            element={
              <ProtectedRoute>
                <DashboardLayout />
              </ProtectedRoute>
            }
          >
            {/* Common */}
            <Route path="profile" element={<ProfilePage />} />
            <Route
              path="notifications"
              element={
                <ProtectedRoute allowedRoles={['Oktato', 'Portas']}>
                  <NotificationsPage />
                </ProtectedRoute>
              }
            />
            <Route path="rooms" element={<RoomsPage />} />
            <Route path="tickets" element={<TicketsPage />} />

            {/* Oktato */}
            <Route
              path="my-bookings"
              element={
                <ProtectedRoute allowedRoles={['Oktato']}>
                  <MyBookingsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="new-booking"
              element={
                <ProtectedRoute allowedRoles={['Oktato', 'Adminisztrator']}>
                  <NewBookingPage />
                </ProtectedRoute>
              }
            />

            {/* Portas */}
            <Route
              path="key-issue"
              element={
                <ProtectedRoute allowedRoles={['Portas']}>
                  <KeyIssuePage />
                </ProtectedRoute>
              }
            />
            <Route
              path="key-return"
              element={
                <ProtectedRoute allowedRoles={['Portas']}>
                  <KeyReturnPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="key-movements"
              element={
                <ProtectedRoute allowedRoles={['Portas', 'Adminisztrator', 'UzemeltetesiIgazgato']}>
                  <KeyMovementsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="active-keys"
              element={
                <ProtectedRoute allowedRoles={['Portas', 'Adminisztrator', 'UzemeltetesiIgazgato']}>
                  <ActiveKeysPage />
                </ProtectedRoute>
              }
            />

            {/* Admin */}
            <Route
              path="admin"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator', 'UzemeltetesiIgazgato']}>
                  <AdminDashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="users"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator']}>
                  <UsersPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="keys"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator']}>
                  <KeysPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="bookings"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator', 'Portas', 'UzemeltetesiIgazgato']}>
                  <BookingsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="maintenance"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator']}>
                  <MaintenancePage />
                </ProtectedRoute>
              }
            />
            <Route
              path="reports"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator', 'UzemeltetesiIgazgato']}>
                  <ReportsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="audit"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator', 'UzemeltetesiIgazgato']}>
                  <AuditLogPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="parameters"
              element={
                <ProtectedRoute allowedRoles={['Adminisztrator', 'UzemeltetesiIgazgato']}>
                  <ParametersPage />
                </ProtectedRoute>
              }
            />
          </Route>

          {/* Ismeretlen cím: olvasható 404-es oldal (átirányítás helyett) */}
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </ConfirmProvider>
      </AuthProvider>
      </ErrorBoundary>
    </BrowserRouter>
  )
}
