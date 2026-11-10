import { useState, useEffect } from 'react'
import { BarChart2, Calendar, TrendingUp, Award, DoorOpen, Key, RefreshCw } from 'lucide-react'
import client from '../../api/client'

interface WeeklyReport {
  hetKezdete?: string
  hetVege?: string
  osszesKulcskiadas: number
  osszesKulcsvisszavetel: number
  osszesKesedelmesFoglalas: number
  osszesNoShow: number
  termenkintiForgalom?: Array<{
    teremId: number
    teremNev: string
    foglalasok: number
    kulcskiadasok: number
  }>
}

interface RoomUtilization {
  kezdet?: string
  veg?: string
  osszesFoglaltOra: number
  osszesRendelkezesreAlloOra: number
  kihasznaltsagSzazalek: number
  termek?: Array<{
    teremId: number
    teremNev: string
    foglaltOra: number
    rendelkezesreAlloOra: number
    kihasznaltsagSzazalek: number
  }>
}

interface ReliabilityReport {
  tanev?: string
  esemenyekSzama: number
  erintettOktatokSzama: number
  atlagosPontszam: number
  esemenytipusok?: Array<{
    esemenyTipus: string
    esemenyekSzama: number
    osszesPont: number
  }>
}

type TabType = 'weekly' | 'utilization' | 'reliability'

export default function ReportsPage() {
  const [activeTab, setActiveTab] = useState<TabType>('weekly')
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [retryVersion, setRetryVersion] = useState(0)

  const [weeklyData, setWeeklyData] = useState<WeeklyReport | null>(null)
  const [utilizationData, setUtilizationData] = useState<RoomUtilization | null>(null)
  const [reliabilityData, setReliabilityData] = useState<ReliabilityReport | null>(null)

  useEffect(() => {
    let ignore = false

    if (activeTab === 'weekly') {
      client
        .get<WeeklyReport>('/reports/weekly')
        .then((res) => {
          if (!ignore) {
            if (!Number.isFinite(res.data.osszesKulcskiadas) ||
                !Number.isFinite(res.data.osszesKulcsvisszavetel) ||
                !Number.isFinite(res.data.osszesKesedelmesFoglalas) ||
                !Number.isFinite(res.data.osszesNoShow)) {
              throw new Error('A heti riport válasza hiányos.')
            }
            setWeeklyData(res.data)
          }
        })
        .catch(() => {
          if (!ignore) {
            setWeeklyData(null)
            setError('A heti riport betöltése nem sikerült.')
          }
        })
        .finally(() => {
          if (!ignore) setIsLoading(false)
        })
    } else if (activeTab === 'utilization') {
      client
        .get<RoomUtilization>('/reports/room-utilization')
        .then((res) => {
          if (!ignore) {
            if (!Number.isFinite(res.data.kihasznaltsagSzazalek) ||
                !Number.isFinite(res.data.osszesFoglaltOra) ||
                !Number.isFinite(res.data.osszesRendelkezesreAlloOra)) {
              throw new Error('A kihasználtsági riport válasza hiányos.')
            }
            setUtilizationData(res.data)
          }
        })
        .catch(() => {
          if (!ignore) {
            setUtilizationData(null)
            setError('A kihasználtsági riport betöltése nem sikerült.')
          }
        })
        .finally(() => {
          if (!ignore) setIsLoading(false)
        })
    } else if (activeTab === 'reliability') {
      client
        .get<ReliabilityReport>('/reports/reliability')
        .then((res) => {
          if (!ignore) {
            if (!Number.isFinite(res.data.atlagosPontszam) ||
                !Number.isFinite(res.data.esemenyekSzama) ||
                !Number.isFinite(res.data.erintettOktatokSzama)) {
              throw new Error('A megbízhatósági riport válasza hiányos.')
            }
            setReliabilityData(res.data)
          }
        })
        .catch(() => {
          if (!ignore) {
            setReliabilityData(null)
            setError('A megbízhatósági riport betöltése nem sikerült.')
          }
        })
        .finally(() => {
          if (!ignore) setIsLoading(false)
        })
    }

    return () => {
      ignore = true
    }
  }, [activeTab, retryVersion])

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <BarChart2 size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Intézményi Riportok</span>
          </h1>
          <p className="page-subtitle">Kulcsforgalmi, kihasználtsági és minőségi statisztikák</p>
        </div>
      </div>

      {/* Fülek */}
      <div className="report-tabs">
        <button
          type="button"
          className={`report-tab-btn ${activeTab === 'weekly' ? 'active' : ''}`}
          onClick={() => {
            setActiveTab('weekly')
            setError('')
            setIsLoading(true)
            setRetryVersion((version) => version + 1)
          }}
        >
          <Calendar size={14} strokeWidth={1.5} />
          <span>Heti kulcs- és teremforgalom</span>
        </button>
        <button
          type="button"
          className={`report-tab-btn ${activeTab === 'utilization' ? 'active' : ''}`}
          onClick={() => {
            setActiveTab('utilization')
            setError('')
            setIsLoading(true)
            setRetryVersion((version) => version + 1)
          }}
        >
          <DoorOpen size={14} strokeWidth={1.5} />
          <span>Teremkihasználtság</span>
        </button>
        <button
          type="button"
          className={`report-tab-btn ${activeTab === 'reliability' ? 'active' : ''}`}
          onClick={() => {
            setActiveTab('reliability')
            setError('')
            setIsLoading(true)
            setRetryVersion((version) => version + 1)
          }}
        >
          <Award size={14} strokeWidth={1.5} />
          <span>Megbízhatósági statisztika</span>
        </button>
      </div>

      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : error ? (
        <div className="alert alert-error" role="alert" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
          <span>{error}</span>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => {
              setError('')
              setIsLoading(true)
              setRetryVersion((version) => version + 1)
            }}
          >
            <RefreshCw size={14} strokeWidth={1.5} />
            <span>Újrapróbálás</span>
          </button>
        </div>
      ) : (
        <>
          {activeTab === 'weekly' && (
            <div>
              <div className="stats-grid">
                <div className="stat-card">
                  <div className="stat-card-icon">
                    <Key size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">{weeklyData?.osszesKulcskiadas ?? '—'}</div>
                    <div className="stat-card-label">Kulcskiadás ezen a héten</div>
                  </div>
                </div>

                <div className="stat-card">
                  <div className="stat-card-icon">
                    <Key size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">{weeklyData?.osszesKulcsvisszavetel ?? '—'}</div>
                    <div className="stat-card-label">Visszavételezett kulcs</div>
                  </div>
                </div>

                <div className="stat-card">
                  <div className="stat-card-icon">
                    <TrendingUp size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">{weeklyData?.osszesKesedelmesFoglalas ?? '—'}</div>
                    <div className="stat-card-label">Késedelmes leadás</div>
                  </div>
                </div>

                <div className="stat-card">
                  <div className="stat-card-icon">
                    <Calendar size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">{weeklyData?.osszesNoShow ?? '—'}</div>
                    <div className="stat-card-label">No-Show esemény</div>
                  </div>
                </div>
              </div>

              {weeklyData?.termenkintiForgalom && (
                <div className="table-container" style={{ marginTop: 20 }}>
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Terem azonosító</th>
                        <th>Foglalások száma</th>
                        <th>Kulcskiadások száma</th>
                      </tr>
                    </thead>
                    <tbody>
                      {weeklyData.termenkintiForgalom.length === 0 ? (
                        <tr>
                          <td colSpan={3} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 30 }}>
                            Erre a hétre még nincs forgalmi adat.
                          </td>
                        </tr>
                      ) : (
                        weeklyData.termenkintiForgalom.map((item) => (
                          <tr key={item.teremId}>
                            <td style={{ fontWeight: 600 }}>{item.teremNev}</td>
                            <td>{item.foglalasok} db</td>
                            <td>{item.kulcskiadasok} db</td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}

          {activeTab === 'utilization' && (
            <div>
              <div className="stats-grid">
                <div className="stat-card">
                  <div className="stat-card-icon">
                    <DoorOpen size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">
                      {utilizationData?.kihasznaltsagSzazalek?.toFixed(1) ?? '—'}%
                    </div>
                    <div className="stat-card-label">Átlagos kihasználtság</div>
                  </div>
                </div>

                <div className="stat-card">
                  <div className="stat-card-icon">
                    <Calendar size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">
                      {utilizationData?.osszesFoglaltOra?.toFixed(1) ?? '—'} óra
                    </div>
                    <div className="stat-card-label">Összes foglalt oktatási óra</div>
                  </div>
                </div>
              </div>

              {utilizationData?.termek && (
                <div className="table-container" style={{ marginTop: 20 }}>
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Terem</th>
                        <th>Foglalt órák</th>
                        <th>Rendelkezésre álló órák</th>
                        <th>Kihasználtság</th>
                      </tr>
                    </thead>
                    <tbody>
                      {utilizationData.termek.map((t) => (
                        <tr key={t.teremId}>
                          <td style={{ fontWeight: 600 }}>{t.teremNev}</td>
                          <td>{t.foglaltOra.toFixed(1)} óra</td>
                          <td>{t.rendelkezesreAlloOra.toFixed(1)} óra</td>
                          <td>
                            <span className="badge badge-primary">
                              {t.kihasznaltsagSzazalek.toFixed(1)}%
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}

          {activeTab === 'reliability' && (
            <div>
              <div className="stats-grid">
                <div className="stat-card">
                  <div className="stat-card-icon">
                    <Award size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">
                      {reliabilityData?.atlagosPontszam?.toFixed(1) ?? '—'} / 100
                    </div>
                    <div className="stat-card-label">Oktatói átlagos megbízhatóság</div>
                  </div>
                </div>

                <div className="stat-card">
                  <div className="stat-card-icon">
                    <TrendingUp size={18} strokeWidth={1.5} />
                  </div>
                  <div className="stat-card-body">
                    <div className="stat-card-value">{reliabilityData?.esemenyekSzama ?? '—'}</div>
                    <div className="stat-card-label">Rögzített megbízhatósági esemény</div>
                  </div>
                </div>
              </div>
            </div>
          )}
        </>
      )}

      <style>{`
        .report-tabs {
          display: flex;
          align-items: center;
          gap: 8px;
          margin-bottom: 24px;
          border-bottom: 1px solid var(--color-gray-200);
          padding-bottom: 12px;
        }

        .report-tab-btn {
          display: inline-flex;
          align-items: center;
          gap: 8px;
          padding: 8px 16px;
          border-radius: var(--border-radius-sm);
          font-size: 13px;
          font-weight: 500;
          color: var(--color-gray-600);
          background-color: transparent;
          border: 1px solid transparent;
          transition: all 0.15s ease;
          cursor: pointer;
        }

        .report-tab-btn:hover {
          background-color: var(--color-gray-100);
          color: var(--color-gray-900);
        }

        .report-tab-btn.active {
          background-color: var(--color-primary-light);
          color: var(--color-primary-hover);
          border-color: var(--color-primary-border);
          font-weight: 600;
        }
      `}</style>
    </div>
  )
}
