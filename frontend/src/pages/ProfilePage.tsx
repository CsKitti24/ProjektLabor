import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { User as UserIcon, LogOut, Shield, Mail, Award, RefreshCw } from 'lucide-react'
import { useAuth, type UserRole } from '../auth/AuthContext'
import client from '../api/client'

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
