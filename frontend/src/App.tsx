import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import ProtectedRoute from './components/ProtectedRoute'

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

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
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
            <Route path="rooms" element={<RoomsPage />} />
            <Route path="tickets" element={<TicketsPage />} />

            {/* Oktato */}
            <Route
              path="my-bookings"
              element={
                <ProtectedRoute allowedRoles={['Oktato', 'Adminisztrator']}>
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
                <ProtectedRoute allowedRoles={['Portas', 'Adminisztrator']}>
                  <KeyIssuePage />
                </ProtectedRoute>
              }
            />
            <Route
              path="key-return"
              element={
                <ProtectedRoute allowedRoles={['Portas', 'Adminisztrator']}>
                  <KeyReturnPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="key-movements"
              element={
                <ProtectedRoute allowedRoles={['Portas', 'Adminisztrator']}>
                  <KeyMovementsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="active-keys"
              element={
                <ProtectedRoute allowedRoles={['Portas', 'Adminisztrator']}>
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
                <ProtectedRoute allowedRoles={['Adminisztrator']}>
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
          </Route>

          {/* Fallback */}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}
