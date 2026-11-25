import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { User as UserIcon, LogOut, Shield, Mail, Award, RefreshCw, KeyRound } from 'lucide-react'
import axios from 'axios'
import { useAuth, type UserRole } from '../auth/AuthContext'
import client from '../api/client'
import { formatDateTime } from '../utils/dateTime'

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

interface ReliabilityEvent {
  id: number
  esemenyTipus: string
  pont: number
  esemenyIdopontja: string
  indoklas?: string | null
}

const EVENT_LABELS: Record<string, string> = {
  LeNemAdott: 'A kulcs a napi zárásig nem került vissza',
  'Késedelmesen leadott': 'Késedelmes leadás',
  'Manuális korrekció': 'Kézi korrekció',
}

interface ReliabilityData {
  userId: number
  nev: string
  tanev: string
  pontszam: number
  kovetkezmeny: string
}

export default function ProfilePage() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [reliability, setReliability] = useState<ReliabilityData | null>(null)
  const [isLoadingReliability, setIsLoadingReliability] = useState(false)
  const [reliabilityError, setReliabilityError] = useState('')
  const canViewReliability = user?.role === 'Oktato'
  // A pontszám okai (az aktuális tanév eseményei): csak kérésre töltjük be.
  const [events, setEvents] = useState<ReliabilityEvent[] | null>(null)
  const [eventsError, setEventsError] = useState('')
  const [showEvents, setShowEvents] = useState(false)

  const toggleEvents = async () => {
    if (showEvents) {
      setShowEvents(false)
      return
    }
    setShowEvents(true)
    if (events !== null) return
    try {
      const response = await client.get<ReliabilityEvent[]>('/reliability/my/events')
      setEvents(Array.isArray(response.data) ? response.data : [])
      setEventsError('')
    } catch {
      setEventsError('Az események betöltése nem sikerült.')
    }
  }

  const loadReliability = useCallback(async () => {
    if (!canViewReliability) return

    setIsLoadingReliability(true)
    setReliabilityError('')
    setReliability(null)
    try {
      const response = await client.get<ReliabilityData>('/reliability/my')
      if (!Number.isFinite(response.data.pontszam)) {
        throw new Error('A megbízhatósági válasz formátuma érvénytelen.')
      }
      setReliability(response.data)
    } catch {
      setReliabilityError('A megbízhatósági pontszám betöltése nem sikerült. Próbáld újra.')
    } finally {
      setIsLoadingReliability(false)
    }
  }, [canViewReliability])

  useEffect(() => {
    if (canViewReliability) {
      void Promise.resolve().then(() => loadReliability())
    }
  }, [canViewReliability, loadReliability])

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [isChangingPassword, setIsChangingPassword] = useState(false)
  const [passwordError, setPasswordError] = useState('')
  const [passwordSuccess, setPasswordSuccess] = useState('')

  const handleChangePassword = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setPasswordError('')
    setPasswordSuccess('')

    if (newPassword.length < 12) {
      setPasswordError('Az új jelszónak legalább 12 karakterből kell állnia.')
      return
    }
    if (newPassword !== confirmPassword) {
      setPasswordError('Az új jelszó és a megerősítés nem egyezik.')
      return
    }

    setIsChangingPassword(true)
    try {
      await client.post('/auth/change-password', {
        jelenlegiJelszo: currentPassword,
        ujJelszo: newPassword,
      })
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setPasswordSuccess('A jelszó sikeresen megváltozott.')
    } catch (error) {
      const message = axios.isAxiosError<{ message?: string }>(error) ? error.response?.data?.message : undefined
      setPasswordError(message || 'A jelszó módosítása nem sikerült. Próbáld újra.')
    } finally {
      setIsChangingPassword(false)
    }
  }

  const [pinPassword, setPinPassword] = useState('')
  const [newPin, setNewPin] = useState('')
  const [isSavingPin, setIsSavingPin] = useState(false)
  const [pinError, setPinError] = useState('')
  const [pinSuccess, setPinSuccess] = useState('')

  const handleSetPin = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setPinError('')
    setPinSuccess('')

    if (!/^[0-9]{4,8}$/.test(newPin)) {
      setPinError('A PIN-kód 4–8 számjegyből állhat.')
      return
    }

    setIsSavingPin(true)
    try {
      await client.put('/auth/pin', { jelenlegiJelszo: pinPassword, pin: newPin })
      setPinPassword('')
      setNewPin('')
      setPinSuccess('A kulcsátvételi PIN-kód beállítva.')
    } catch (error) {
      const message = axios.isAxiosError<{ message?: string }>(error) ? error.response?.data?.message : undefined
      setPinError(message || 'A PIN-kód beállítása nem sikerült. Próbáld újra.')
    } finally {
      setIsSavingPin(false)
    }
  }

  const handleLogout = () => {
    logout()
    navigate('/')
  }

  if (!user) return null

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <UserIcon size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Felhasználói profil</span>
          </h1>
          <p className="page-subtitle">Személyes adatok és rendszerbeli jogosultságok</p>
        </div>
      </div>

      <div className="profile-grid">
        {/* Fő profil kártya */}
        <div className="card profile-main-card">
          <div className="profile-header-section">
            <div className="profile-avatar">
              <UserIcon size={28} strokeWidth={1.5} color="var(--color-primary)" />
            </div>
            <div className="profile-header-text">
              <h2 className="profile-name">{user.nev || user.username}</h2>
              <div className="profile-badge-row">
                <span className="badge badge-primary">{getRoleLabel(user.role)}</span>
                <span className="profile-id-text">ID: #{user.userId}</span>
              </div>
            </div>
          </div>

          <div className="profile-details-list">
            <div className="profile-detail-item">
              <span className="profile-detail-label">Felhasználónév</span>
              <span className="profile-detail-value">{user.username}</span>
            </div>

            <div className="profile-detail-item">
              <span className="profile-detail-label">E-mail cím</span>
              <span className="profile-detail-value">
                <Mail size={13} strokeWidth={1.5} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                {user.email || '—'}
              </span>
            </div>

            <div className="profile-detail-item">
              <span className="profile-detail-label">Szerepkör (RBAC)</span>
              <span className="profile-detail-value">
                <Shield size={13} strokeWidth={1.5} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                {getRoleLabel(user.role)}
              </span>
            </div>
          </div>

          <div className="profile-actions">
            <button
              type="button"
              className="btn btn-danger btn-sm"
              onClick={handleLogout}
            >
              <LogOut size={14} strokeWidth={1.5} />
              <span>Kijelentkezés</span>
            </button>
          </div>
        </div>

        {canViewReliability && (
          <div className="card profile-stat-card">
            <div className="profile-stat-header">
              <div className="profile-stat-icon-wrap">
                <Award size={18} strokeWidth={1.5} color="var(--color-primary)" />
              </div>
              <h3 className="profile-stat-title">Megbízhatósági hibapont</h3>
            </div>

            <div className="profile-stat-body">
              {isLoadingReliability ? (
                <div style={{ padding: 20, textAlign: 'center' }}>
                  <span className="spinner spinner-primary" />
                </div>
              ) : reliability ? (
                <>
                  <div className="profile-score-display">
                    <span className="profile-score-number">{reliability.pontszam}</span>
                    <span className="profile-score-unit">/ 100 pont</span>
                  </div>

                  <div className="profile-stat-note">
                    {reliability.pontszam < 10 ? (
                      <span className="badge badge-success">{reliability.kovetkezmeny}</span>
                    ) : (
                      <span className="badge badge-warning">{reliability.kovetkezmeny}</span>
                    )}
                  </div>

                  <p className="profile-stat-desc">
                    A magasabb pont több rögzített hibát jelent. Tanév: {reliability.tanev}.
                  </p>
                  <button type="button" className="btn btn-ghost btn-sm" onClick={() => void toggleEvents()} aria-expanded={showEvents}>
                    {showEvents ? 'Események elrejtése' : 'Miért kaptam pontot?'}
                  </button>
                  {showEvents && (
                    eventsError ? (
                      <div className="alert alert-error" role="alert">{eventsError}</div>
                    ) : events === null ? (
                      <span className="spinner spinner-primary" />
                    ) : events.length === 0 ? (
                      <p className="profile-stat-desc">Ebben a tanévben nincs rögzített eseményed.</p>
                    ) : (
                      <ul aria-label="Megbízhatósági eseményeim" style={{ listStyle: 'none', padding: 0, margin: 0, display: 'grid', gap: 6 }}>
                        {events.map((item) => (
                          <li key={item.id} style={{ fontSize: 12.5, borderTop: '1px solid var(--color-gray-100)', paddingTop: 6 }}>
                            <strong>{item.pont > 0 ? `+${item.pont}` : item.pont} pont</strong> – {EVENT_LABELS[item.esemenyTipus] ?? item.esemenyTipus}
                            <div style={{ color: 'var(--color-gray-500)' }}>
                              {formatDateTime(item.esemenyIdopontja)}{item.indoklas ? ` · ${item.indoklas}` : ''}
                            </div>
                          </li>
                        ))}
                      </ul>
                    )
                  )}
                </>
              ) : reliabilityError ? (
                <div className="alert alert-error" role="alert">
                  <span>{reliabilityError}</span>
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={() => void loadReliability()}
                  >
                    <RefreshCw size={14} strokeWidth={1.5} />
                    <span>Újrapróbálás</span>
                  </button>
                </div>
              ) : (
                <div className="alert alert-error" role="alert">A megbízhatósági pontszám nem érhető el.</div>
              )}
            </div>
          </div>
        )}
        <div className="card profile-stat-card">
          <div className="profile-stat-header">
            <div className="profile-stat-icon-wrap">
              <KeyRound size={18} strokeWidth={1.5} color="var(--color-primary)" />
            </div>
            <h3 className="profile-stat-title">Jelszó módosítása</h3>
          </div>

          <form onSubmit={(event) => void handleChangePassword(event)} noValidate>
            <div className="form-group">
              <label className="form-label" htmlFor="current-password">Jelenlegi jelszó</label>
              <input
                id="current-password"
                type="password"
                className="form-input"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label" htmlFor="new-password">Új jelszó (legalább 12 karakter)</label>
              <input
                id="new-password"
                type="password"
                className="form-input"
                autoComplete="new-password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label" htmlFor="confirm-password">Új jelszó megerősítése</label>
              <input
                id="confirm-password"
                type="password"
                className="form-input"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
                required
              />
            </div>

            {passwordError && (
              <div className="alert alert-error" role="alert">{passwordError}</div>
            )}
            {passwordSuccess && (
              <div className="alert alert-success" role="status">{passwordSuccess}</div>
            )}

            <button
              type="submit"
              className="btn btn-primary btn-sm"
              disabled={isChangingPassword || !currentPassword || !newPassword || !confirmPassword}
            >
              {isChangingPassword ? <span className="spinner" /> : <KeyRound size={14} strokeWidth={1.5} />}
              <span>Jelszó módosítása</span>
            </button>
          </form>
        </div>
        {canViewReliability && (
          <div className="card profile-stat-card">
            <div className="profile-stat-header">
              <div className="profile-stat-icon-wrap">
                <KeyRound size={18} strokeWidth={1.5} color="var(--color-primary)" />
              </div>
              <h3 className="profile-stat-title">Kulcsátvételi PIN-kód</h3>
            </div>
            <p className="profile-stat-desc">
              A portán a kulcs átvételekor ezt a PIN-kódot kell megadnod.
            </p>

            <form onSubmit={(event) => void handleSetPin(event)} noValidate>
              <div className="form-group">
                <label className="form-label" htmlFor="pin-password">Jelenlegi jelszó (megerősítéshez)</label>
                <input
                  id="pin-password"
                  type="password"
                  className="form-input"
                  autoComplete="current-password"
                  value={pinPassword}
                  onChange={(event) => setPinPassword(event.target.value)}
                  required
                />
              </div>
              <div className="form-group">
                <label className="form-label" htmlFor="new-pin">Új PIN-kód (4–8 számjegy)</label>
                <input
                  id="new-pin"
                  type="password"
                  inputMode="numeric"
                  className="form-input"
                  autoComplete="new-password"
                  maxLength={8}
                  value={newPin}
                  onChange={(event) => setNewPin(event.target.value)}
                  required
                />
              </div>

              {pinError && (
                <div className="alert alert-error" role="alert">{pinError}</div>
              )}
              {pinSuccess && (
                <div className="alert alert-success" role="status">{pinSuccess}</div>
              )}

              <button
                type="submit"
                className="btn btn-primary btn-sm"
                disabled={isSavingPin || !pinPassword || !newPin}
              >
                {isSavingPin ? <span className="spinner" /> : <KeyRound size={14} strokeWidth={1.5} />}
                <span>PIN-kód mentése</span>
              </button>
            </form>
          </div>
        )}
      </div>

      <style>{`
        .profile-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
          gap: 20px;
          max-width: 860px;
        }

        .profile-main-card {
          display: flex;
          flex-direction: column;
          gap: 20px;
        }

        .profile-header-section {
          display: flex;
          align-items: center;
          gap: 16px;
          padding-bottom: 16px;
          border-bottom: 1px solid var(--color-gray-100);
        }

        .profile-avatar {
          width: 52px;
          height: 52px;
          border-radius: 50%;
          background-color: var(--color-primary-light);
          border: 1px solid var(--color-primary-border);
          display: flex;
          align-items: center;
          justify-content: center;
          flex-shrink: 0;
        }

        .profile-name {
          font-size: 17px;
          font-weight: 600;
          color: var(--color-gray-900);
        }

        .profile-badge-row {
          display: flex;
          align-items: center;
          gap: 8px;
          margin-top: 4px;
        }

        .profile-id-text {
          font-size: 12px;
          color: var(--color-gray-400);
        }

        .profile-details-list {
          display: flex;
          flex-direction: column;
          gap: 12px;
        }

        .profile-detail-item {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 8px 0;
          border-bottom: 1px solid var(--color-gray-50);
        }

        .profile-detail-label {
          font-size: 13px;
          color: var(--color-gray-500);
        }

        .profile-detail-value {
          font-size: 13.5px;
          font-weight: 500;
          color: var(--color-gray-800);
        }

        .profile-actions {
          padding-top: 8px;
        }

        .profile-stat-card {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }

        .profile-stat-header {
          display: flex;
          align-items: center;
          gap: 10px;
        }

        .profile-stat-icon-wrap {
          width: 32px;
          height: 32px;
          border-radius: var(--border-radius-sm);
          background-color: var(--color-primary-light);
          display: flex;
          align-items: center;
          justify-content: center;
        }

        .profile-stat-title {
          font-size: 15px;
          font-weight: 600;
          color: var(--color-gray-900);
        }

        .profile-score-display {
          display: flex;
          align-items: baseline;
          gap: 8px;
          margin-bottom: 8px;
        }

        .profile-score-number {
          font-size: 36px;
          font-weight: 700;
          color: var(--color-gray-900);
          line-height: 1;
        }

        .profile-score-unit {
          font-size: 13px;
          color: var(--color-gray-500);
        }

        .profile-stat-note {
          margin-bottom: 12px;
        }

        .profile-stat-desc {
          font-size: 12.5px;
          color: var(--color-gray-500);
          line-height: 1.45;
        }
      `}</style>
    </div>
  )
}
