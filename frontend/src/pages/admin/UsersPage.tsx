import { useState, useEffect } from 'react'
import { Users, AlertCircle, Check, Shield, UserPlus, Search } from 'lucide-react'
import client from '../../api/client'
import Pagination, { DEFAULT_PAGE_SIZE } from '../../components/Pagination'
import CreateUserModal from './CreateUserModal'
import { formatDateTime } from '../../utils/dateTime'

interface UserItem {
  id: number | string
  username: string
  nev?: string
  role: string
  email?: string
  aktiv?: boolean
  hasPin?: boolean
  foglalasFelfuggesztveEddig?: string | null
  felfuggesztesOka?: string | null
}

function isSuspended(user: UserItem): boolean {
  return !!user.foglalasFelfuggesztveEddig && new Date(user.foglalasFelfuggesztveEddig).getTime() > Date.now()
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
  const [editNev, setEditNev] = useState('')
  const [editEmail, setEditEmail] = useState('')
  const [showCreate, setShowCreate] = useState(false)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [roleFilter, setRoleFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [reloadKey, setReloadKey] = useState(0)
  const [suspendingId, setSuspendingId] = useState<string | number | null>(null)
  const [suspendUntil, setSuspendUntil] = useState('')
  const [suspendReason, setSuspendReason] = useState('')

  // A keresés gépelés közben, rövid várakozás után frissül (nem minden billentyűleütésre).
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(timer)
  }, [searchInput])

  useEffect(() => {
    let ignore = false
    client
      .get<{ items?: UserItem[]; totalCount?: number } | UserItem[]>('/users', {
        params: {
          page,
          pageSize: DEFAULT_PAGE_SIZE,
          kereses: search || undefined,
          role: roleFilter || undefined,
          aktiv: statusFilter === '' ? undefined : statusFilter === 'active',
        },
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
  }, [page, search, roleFilter, statusFilter, reloadKey])

  const handleEditStart = (user: UserItem) => {
    setEditingId(user.id)
    setEditRole(user.role)
    setEditNev(user.nev ?? '')
    setEditEmail(user.email ?? '')
    setEditPin('')
    setEditPassword('')
    setEditPasswordConfirmation('')
  }

  const handleSavePassword = async (id: string | number) => {
    if (editPassword.length < 12) {
      setError('A jelszónak legalább 12 karakterből kell állnia.')
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
      setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, hasPin: true } : u)))
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

  const handleSaveDetails = async (user: UserItem) => {
    if (!editNev.trim() || !editEmail.trim()) {
      setError('A név és az e-mail cím nem lehet üres.')
      return
    }

    setIsSaving(true)
    try {
      await client.put(`/users/${user.id}`, { nev: editNev.trim(), email: editEmail.trim() })
      setUsers((prev) => prev.map((u) => (u.id === user.id
        ? { ...u, nev: editNev.trim(), email: editEmail.trim().toLowerCase() }
        : u)))
      showSuccess('A felhasználó adatai frissítve.')
    } catch (err: unknown) {
      setError(errorMessage(err, 'Az adatok módosítása sikertelen.'))
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

  const submitSuspension = async (user: UserItem, until: string | null) => {
    if (until !== null && (!until || !suspendReason.trim())) {
      setError('A felfüggesztéshez add meg a végét és az indoklást.')
      return
    }
    setIsSaving(true)
    setError('')
    try {
      const response = await client.put<{ message: string; foglalasFelfuggesztveEddig: string | null; felfuggesztesOka: string | null }>(
        `/users/${user.id}/booking-suspension`,
        { eddig: until, indoklas: until ? suspendReason.trim() : null },
      )
      setUsers((prev) => prev.map((u) => (u.id === user.id
        ? { ...u, foglalasFelfuggesztveEddig: response.data.foglalasFelfuggesztveEddig, felfuggesztesOka: response.data.felfuggesztesOka }
        : u)))
      setSuspendingId(null)
      setSuspendUntil('')
      setSuspendReason('')
      showSuccess(response.data.message)
    } catch (err: unknown) {
      setError(errorMessage(err, 'A foglalási jog módosítása sikertelen.'))
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
        <button type="button" className="btn btn-primary" onClick={() => setShowCreate(true)}>
          <UserPlus size={15} strokeWidth={1.5} />
          <span>Új felhasználó</span>
        </button>
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginBottom: 16 }}>
        <div style={{ position: 'relative', flex: '1 1 240px' }}>
          <Search size={14} strokeWidth={1.5} style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--color-gray-400)' }} />
          <input
            className="form-input"
            style={{ paddingLeft: 30 }}
            aria-label="Keresés név, felhasználónév vagy e-mail szerint"
            placeholder="Keresés név, felhasználónév vagy e-mail szerint…"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </div>
        <select
          className="form-select"
          style={{ flex: '0 1 200px' }}
          aria-label="Szűrés szerepkör szerint"
          value={roleFilter}
          onChange={(e) => { setRoleFilter(e.target.value); setPage(1) }}
        >
          <option value="">Minden szerepkör</option>
          {ROLES.map((r) => <option key={r} value={r}>{getRoleLabel(r)}</option>)}
        </select>
        <select
          className="form-select"
          style={{ flex: '0 1 160px' }}
          aria-label="Szűrés állapot szerint"
          value={statusFilter}
          onChange={(e) => { setStatusFilter(e.target.value); setPage(1) }}
        >
          <option value="">Aktív és inaktív</option>
          <option value="active">Csak aktív</option>
          <option value="inactive">Csak inaktív</option>
        </select>
      </div>

      {showCreate && (
        <CreateUserModal
          onClose={() => setShowCreate(false)}
          onCreated={(message) => {
            setShowCreate(false)
            setPage(1)
            setReloadKey((key) => key + 1)
            showSuccess(message)
          }}
        />
      )}

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
                    {search || roleFilter || statusFilter ? 'Nincs a szűrésnek megfelelő felhasználó.' : 'Nincs felhasználó a rendszerben.'}
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
                          <input
                            aria-label={`${user.nev || user.username} neve`}
                            className="form-input"
                            placeholder="Teljes név"
                            maxLength={100}
                            value={editNev}
                            onChange={(e) => setEditNev(e.target.value)}
                          />
                          <input
                            aria-label={`${user.nev || user.username} e-mail címe`}
                            className="form-input"
                            type="email"
                            placeholder="E-mail cím"
                            maxLength={100}
                            value={editEmail}
                            onChange={(e) => setEditEmail(e.target.value)}
                          />
                          <select
                            aria-label={`${user.nev || user.username} szerepköre`}
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
                            minLength={12}
                            placeholder="Új jelszó (min. 12 karakter)"
                            autoComplete="new-password"
                            value={editPassword}
                            onChange={(e) => setEditPassword(e.target.value)}
                          />
                          <input
                            aria-label={`${user.nev || user.username} új jelszó megerősítése`}
                            className="form-input"
                            type="password"
                            minLength={12}
                            placeholder="Jelszó ismétlése"
                            autoComplete="new-password"
                            value={editPasswordConfirmation}
                            onChange={(e) => setEditPasswordConfirmation(e.target.value)}
                          />
                        </div>
                      ) : (
                        <>
                          <span className="badge badge-primary">
                            <Shield size={11} strokeWidth={1.5} style={{ marginRight: 2 }} />
                            {getRoleLabel(user.role)}
                          </span>
                          {user.role === 'Oktato' && user.hasPin === false && (
                            <span
                              className="badge badge-warning"
                              style={{ marginLeft: 6 }}
                              title="Kulcsot csak beállított PIN-kóddal vehet át."
                            >
                              Nincs PIN
                            </span>
                          )}
                          {user.role === 'Oktato' && isSuspended(user) && (
                            <span
                              className="badge badge-warning"
                              style={{ marginLeft: 6 }}
                              title={user.felfuggesztesOka ?? undefined}
                            >
                              Foglalás felfüggesztve {formatDateTime(user.foglalasFelfuggesztveEddig)}-ig
                            </span>
                          )}
                        </>
                      )}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      {editingId === user.id ? (
                        <div style={{ display: 'inline-flex', gap: 6 }}>
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            disabled={isSaving || editRole === user.role}
                            onClick={() => handleSave(user.id)}
                          >
                            {isSaving ? 'Mentés…' : 'Szerepkör mentése'}
                          </button>
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            disabled={isSaving || (editNev.trim() === (user.nev ?? '') && editEmail.trim().toLowerCase() === (user.email ?? '').toLowerCase())}
                            onClick={() => handleSaveDetails(user)}
                          >
                            Adatok mentése
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
                      ) : suspendingId === user.id ? (
                        <div style={{ display: 'inline-grid', gap: 6, justifyItems: 'end' }}>
                          <input
                            aria-label={`${user.nev || user.username} foglalási felfüggesztés vége`}
                            className="form-input"
                            type="datetime-local"
                            value={suspendUntil}
                            onChange={(e) => setSuspendUntil(e.target.value)}
                          />
                          <input
                            aria-label={`${user.nev || user.username} felfüggesztés indoklása`}
                            className="form-input"
                            placeholder="Indoklás"
                            maxLength={500}
                            value={suspendReason}
                            onChange={(e) => setSuspendReason(e.target.value)}
                          />
                          <div style={{ display: 'inline-flex', gap: 6 }}>
                            <button
                              type="button"
                              className="btn btn-danger btn-sm"
                              disabled={isSaving || !suspendUntil || !suspendReason.trim()}
                              onClick={() => submitSuspension(user, suspendUntil)}
                            >
                              Felfüggesztés
                            </button>
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              onClick={() => setSuspendingId(null)}
                            >
                              Mégse
                            </button>
                          </div>
                        </div>
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
                          {user.role === 'Oktato' && (isSuspended(user) ? (
                            <button
                              type="button"
                              className="btn btn-ghost btn-sm"
                              disabled={isSaving}
                              onClick={() => submitSuspension(user, null)}
                            >
                              Foglalás engedélyezése
                            </button>
                          ) : (
                            <button
                              type="button"
                              className="btn btn-ghost btn-sm"
                              onClick={() => { setSuspendingId(user.id); setSuspendUntil(''); setSuspendReason('') }}
                            >
                              Foglalás felfüggesztése
                            </button>
                          ))}
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
