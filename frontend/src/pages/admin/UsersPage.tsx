import { useState, useEffect } from 'react'
import { Users, AlertCircle, Check, Shield } from 'lucide-react'
import client from '../../api/client'
import Pagination, { DEFAULT_PAGE_SIZE } from '../../components/Pagination'

interface UserItem {
  id: number | string
  username: string
  nev?: string
  role: string
  email?: string
  aktiv?: boolean
}

function errorMessage(err: unknown, fallback: string): string {
  const axErr = err as { response?: { data?: { message?: string } } }
  return axErr.response?.data?.message || fallback
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
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [successMsg, setSuccessMsg] = useState('')
  const [editingId, setEditingId] = useState<string | number | null>(null)
  const [editRole, setEditRole] = useState('')
  const [editPin, setEditPin] = useState('')
  const [editPassword, setEditPassword] = useState('')
  const [editPasswordConfirmation, setEditPasswordConfirmation] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [confirmDeactivateId, setConfirmDeactivateId] = useState<string | number | null>(null)

  useEffect(() => {
    let ignore = false
    client
      .get<{ items?: UserItem[]; totalCount?: number } | UserItem[]>('/users', {
        params: { page, pageSize: DEFAULT_PAGE_SIZE },
      })
      .then((r) => {
        if (!ignore) {
          const list = Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
          setUsers(list)
          setTotalCount(Array.isArray(r.data) ? list.length : (r.data?.totalCount ?? list.length))
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const axErr = err as { response?: { data?: { message?: string } } }
          setError(axErr.response?.data?.message || 'Nem sikerült betölteni a felhasználókat.')
        }
      })
      .finally(() => {
        if (!ignore) setIsLoading(false)
      })

    return () => {
      ignore = true
    }
  }, [page])

  const handleEditStart = (user: UserItem) => {
    setEditingId(user.id)
    setEditRole(user.role)
    setEditPin('')
    setEditPassword('')
    setEditPasswordConfirmation('')
  }

  const handleSavePassword = async (id: string | number) => {
    if (editPassword.length < 8) {
      setError('A jelszónak legalább 8 karakterből kell állnia.')
      return
    }
    if (editPassword !== editPasswordConfirmation) {
      setError('A két jelszó nem egyezik.')
      return
    }

    setIsSaving(true)
    try {
      await client.put(`/users/${id}`, { UjJelszo: editPassword })
      setEditPassword('')
      setEditPasswordConfirmation('')
      setError('')
      setSuccessMsg('A jelszó sikeresen módosítva.')
      setTimeout(() => setSuccessMsg(''), 3000)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A jelszó módosítása sikertelen.')
    } finally {
      setIsSaving(false)
    }
  }

  const handleSavePin = async (id: string | number) => {
    if (!/^[0-9]{4,8}$/.test(editPin)) {
      setError('A PIN-kód 4–8 számjegyből állhat.')
      return
    }

    setIsSaving(true)
    try {
      await client.put(`/users/${id}/pin`, { pin: editPin })
      setEditPin('')
      setError('')
      setSuccessMsg('A kulcsátvételi PIN sikeresen beállítva.')
      setTimeout(() => setSuccessMsg(''), 3000)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A PIN-kód beállítása sikertelen.')
    } finally {
      setIsSaving(false)
    }
  }

  const showSuccess = (message: string) => {
    setError('')
    setSuccessMsg(message)
    setTimeout(() => setSuccessMsg(''), 3000)
  }

  const handleSave = async (id: string | number) => {
    setIsSaving(true)
    try {
      // A backend a ChangeRoleRequestDto.UjRole mezőt várja.
      await client.put(`/users/${id}/role`, { ujRole: editRole })
      setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, role: editRole } : u)))
      setEditingId(null)
      showSuccess('A szerepkör sikeresen frissítve.')
    } catch (err: unknown) {
      setError(errorMessage(err, 'A szerepkör módosítása sikertelen.'))
    } finally {
      setIsSaving(false)
    }
  }

  const handleDeactivate = async (id: string | number) => {
    setIsSaving(true)
    try {
      await client.delete(`/users/${id}`)
      setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, aktiv: false } : u)))
      setConfirmDeactivateId(null)
      showSuccess('A felhasználó inaktiválva lett. Az előzményei megmaradnak, de nem tud bejelentkezni.')
    } catch (err: unknown) {
      setConfirmDeactivateId(null)
      setError(errorMessage(err, 'A felhasználó inaktiválása sikertelen.'))
    } finally {
      setIsSaving(false)
    }
  }

  const handleReactivate = async (id: string | number) => {
    setIsSaving(true)
    try {
      await client.post(`/users/${id}/reactivate`)
      setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, aktiv: true } : u)))
      showSuccess('A felhasználó újra aktív.')
    } catch (err: unknown) {
      setError(errorMessage(err, 'A felhasználó újraaktiválása sikertelen.'))
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
                users.map((user) => {
                  const isInactive = user.aktiv === false
                  return (
                  <tr key={user.id} style={isInactive ? { opacity: 0.6 } : undefined}>
                    <td style={{ color: 'var(--color-gray-500)', fontSize: 12 }}>#{user.id}</td>
                    <td>
                      <div style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>
                        {user.nev || user.username}
                        {isInactive && (
                          <span className="badge badge-neutral" style={{ marginLeft: 8 }}>Inaktív</span>
                        )}
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
                        <div style={{ display: 'grid', gap: 8, maxWidth: 220 }}>
                          <select
                            className="form-select"
                            style={{ padding: '4px 8px', fontSize: 13 }}
                            value={editRole}
                            onChange={(e) => setEditRole(e.target.value)}
                          >
                            {ROLES.map((r) => (
                              <option key={r} value={r}>{getRoleLabel(r)}</option>
                            ))}
                          </select>
                          <input
                            aria-label={`${user.nev || user.username} kulcsátvételi PIN`}
                            className="form-input"
                            type="password"
                            inputMode="numeric"
                            pattern="[0-9]{4,8}"
                            minLength={4}
                            maxLength={8}
                            placeholder="Új PIN (4–8 számjegy)"
                            autoComplete="new-password"
                            value={editPin}
                            onChange={(e) => setEditPin(e.target.value)}
                          />
                          <input
                            aria-label={`${user.nev || user.username} új jelszó`}
                            className="form-input"
                            type="password"
                            minLength={8}
                            placeholder="Új jelszó (min. 8 karakter)"
                            autoComplete="new-password"
                            value={editPassword}
                            onChange={(e) => setEditPassword(e.target.value)}
                          />
                          <input
                            aria-label={`${user.nev || user.username} új jelszó megerősítése`}
                            className="form-input"
                            type="password"
                            minLength={8}
                            placeholder="Jelszó ismétlése"
                            autoComplete="new-password"
                            value={editPasswordConfirmation}
                            onChange={(e) => setEditPasswordConfirmation(e.target.value)}
                          />
                        </div>
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
                            disabled={isSaving || !editPin}
                            onClick={() => handleSavePin(user.id)}
                          >
                            PIN mentése
                          </button>
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            disabled={isSaving || !editPassword || !editPasswordConfirmation}
                            onClick={() => handleSavePassword(user.id)}
                          >
                            Jelszó mentése
                          </button>
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            onClick={() => setEditingId(null)}
                          >
                            Mégse
                          </button>
                        </div>
                      ) : isInactive ? (
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          disabled={isSaving}
                          onClick={() => handleReactivate(user.id)}
                        >
                          Újraaktiválás
                        </button>
                      ) : confirmDeactivateId === user.id ? (
                        <div style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
                          <span style={{ fontSize: 12, color: 'var(--color-gray-600)' }}>Biztosan inaktiválod?</span>
                          <button
                            type="button"
                            className="btn btn-danger btn-sm"
                            disabled={isSaving}
                            onClick={() => handleDeactivate(user.id)}
                          >
                            Igen, inaktiválás
                          </button>
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            onClick={() => setConfirmDeactivateId(null)}
                          >
                            Mégse
                          </button>
                        </div>
                      ) : (
                        <div style={{ display: 'inline-flex', gap: 6 }}>
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            onClick={() => handleEditStart(user)}
                          >
                            Módosítás
                          </button>
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            onClick={() => setConfirmDeactivateId(user.id)}
                          >
                            Inaktiválás
                          </button>
                        </div>
                      )}
                    </td>
                  </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      )}
      <Pagination page={page} pageSize={DEFAULT_PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
