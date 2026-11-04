import { useState, useEffect } from 'react'
import { Users, AlertCircle, Check, Shield } from 'lucide-react'
import client from '../../api/client'

interface UserItem {
  id: number | string
  username: string
  nev?: string
  role: string
  email?: string
}

const ROLES = ['Oktato', 'Portas', 'Adminisztrator', 'UzemeltetesiIgazgato']

function getRoleLabel(role: string): string {
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

export default function UsersPage() {
  const [users, setUsers] = useState<UserItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [successMsg, setSuccessMsg] = useState('')
  const [editingId, setEditingId] = useState<string | number | null>(null)
  const [editRole, setEditRole] = useState('')
  const [isSaving, setIsSaving] = useState(false)

  const fetchUsers = () => {
    setIsLoading(true)
    client
      .get<{ items?: UserItem[] } | UserItem[]>('/users')
      .then((r) => {
        const list = Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
        setUsers(list)
        setError('')
      })
      .catch((err: unknown) => {
        const axErr = err as { response?: { data?: { message?: string } } }
        setError(axErr.response?.data?.message || 'Nem sikerült betölteni a felhasználókat.')
      })
      .finally(() => setIsLoading(false))
  }

  useEffect(() => {
    fetchUsers()
  }, [])

  const handleEditStart = (user: UserItem) => {
    setEditingId(user.id)
    setEditRole(user.role)
  }

  const handleSave = async (id: string | number) => {
    setIsSaving(true)
    try {
      await client.put(`/users/${id}/role`, { role: editRole })
      setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, role: editRole } : u)))
      setEditingId(null)
      setSuccessMsg('A szerepkör sikeresen frissítve.')
      setTimeout(() => setSuccessMsg(''), 3000)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      alert(axErr.response?.data?.message || 'A szerepkör módosítása sikertelen.')
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <Users size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Felhasználók és jogosultságok</span>
          </h1>
          <p className="page-subtitle">Rendszerfelhasználók és RBAC szerepkörök kezelése</p>
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{error}</span>
        </div>
      )}

      {successMsg && (
        <div className="alert alert-success">
          <Check size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{successMsg}</span>
        </div>
      )}

      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : (
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th style={{ width: '8%' }}>ID</th>
                <th style={{ width: '24%' }}>Név / Felhasználónév</th>
                <th style={{ width: '28%' }}>E-mail cím</th>
                <th style={{ width: '25%' }}>Szerepkör</th>
                <th style={{ width: '15%', textAlign: 'right' }}>Művelet</th>
              </tr>
            </thead>
            <tbody>
              {users.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Nincs felhasználó a rendszerben.
                  </td>
                </tr>
              ) : (
                users.map((user) => (
                  <tr key={user.id}>
                    <td style={{ color: 'var(--color-gray-500)', fontSize: 12 }}>#{user.id}</td>
                    <td>
                      <div style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                        {user.nev || user.username}
                      </div>
                      {user.nev && user.nev !== user.username && (
                        <div style={{ fontSize: 12, color: 'var(--color-gray-500)' }}>
                          {user.username}
                        </div>
                      )}
                    </td>
                    <td style={{ color: 'var(--color-gray-600)' }}>{user.email ?? '—'}</td>
                    <td>
                      {editingId === user.id ? (
                        <select
                          className="form-select"
                          style={{ padding: '4px 8px', fontSize: 13, maxWidth: 200 }}
                          value={editRole}
                          onChange={(e) => setEditRole(e.target.value)}
                        >
                          {ROLES.map((r) => (
                            <option key={r} value={r}>
                              {getRoleLabel(r)}
                            </option>
                          ))}
                        </select>
                      ) : (
                        <span className="badge badge-primary">
                          <Shield size={11} strokeWidth={1.5} style={{ marginRight: 2 }} />
                          {getRoleLabel(user.role)}
                        </span>
                      )}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      {editingId === user.id ? (
                        <div style={{ display: 'inline-flex', gap: 6 }}>
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            disabled={isSaving}
                            onClick={() => handleSave(user.id)}
                          >
                            {isSaving ? 'Mentés…' : 'Mentés'}
                          </button>
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            onClick={() => setEditingId(null)}
                          >
                            Mégse
                          </button>
                        </div>
                      ) : (
                        <button
                          type="button"
                          className="btn btn-ghost btn-sm"
                          onClick={() => handleEditStart(user)}
                        >
                          Módosítás
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
