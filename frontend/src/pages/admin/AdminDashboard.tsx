import { useState, useEffect, useCallback } from 'react'
import { Link } from 'react-router-dom'
import {
  LayoutDashboard,
  DoorOpen,
  Key,
  CalendarDays,
  FileText,
  BarChart2,
  ArrowRight,
  RefreshCw,
} from 'lucide-react'
import client from '../../api/client'
import { useAuth } from '../../auth/AuthContext'

interface Stats {
  rooms: number
  activeKeys: number
  todayBookings: number
  openTickets: number
}

interface BookingListResponse {
  totalCount: number
}

interface TicketListResponse {
  items: Ticket[]
  totalCount: number
}

interface Ticket {
  statusz?: string
}

async function fetchAllTickets(): Promise<Ticket[]> {
  const pageSize = 100
  const tickets: Ticket[] = []
  let hasMore = true
  let page = 1

  while (hasMore) {
    const response = await client.get<TicketListResponse>('/tickets', {
      params: { page, pageSize },
    })
    if (!Array.isArray(response.data.items) || !Number.isInteger(response.data.totalCount)) {
      throw new Error('A hibajegy-válasz formátuma érvénytelen.')
    }

    tickets.push(...response.data.items)
    hasMore = tickets.length < response.data.totalCount
    if (hasMore && response.data.items.length === 0) {
      throw new Error('A hibajegyek lapozása nem teljes.')
    }
    page += 1
  }

  return tickets
}

export default function AdminDashboard() {
  const { user } = useAuth()
  const [stats, setStats] = useState<Stats | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')

  const loadDashboard = useCallback(async () => {
    setIsLoading(true)
    setError('')
    setStats(null)

    try {
      const now = new Date()
      const startOfDay = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()))
      const endOfDay = new Date(startOfDay.getTime() + 24 * 60 * 60 * 1000)
      const [roomsRes, keysRes, bookingsRes, ticketsData] = await Promise.all([
        client.get<Array<unknown>>('/rooms'),
        client.get<Array<{ jelenlegiAllapot?: string }>>('/keys'),
        client.get<BookingListResponse>('/bookings', {
          params: {
            page: 1,
            pageSize: 1,
            tol: startOfDay.toISOString(),
            ig: endOfDay.toISOString(),
          },
        }),
        fetchAllTickets(),
      ])

      if (!Array.isArray(roomsRes.data) || !Array.isArray(keysRes.data) ||
          !Number.isInteger(bookingsRes.data.totalCount)) {
        throw new Error('Az áttekintő adatok formátuma érvénytelen.')
      }

      const activeKeys = keysRes.data.filter((key) => key.jelenlegiAllapot === 'Kiadva').length
      const openTickets = ticketsData.filter(
        (ticket) => !['Zarva', 'Lezarva', 'Megoldva', 'Closed', 'Resolved'].includes(ticket.statusz ?? '')
      ).length

      setStats({
        rooms: roomsRes.data.length,
        activeKeys,
        todayBookings: bookingsRes.data.totalCount,
        openTickets,
      })
    } catch {
      setError('Az áttekintő adatok betöltése nem sikerült. Ellenőrizd a kapcsolatot, majd próbáld újra.')
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    void Promise.resolve().then(() => loadDashboard())
  }, [loadDashboard])

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <LayoutDashboard size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Rendszer áttekintés</span>
          </h1>
          <p className="page-subtitle">
            Üdvözöljük, {user?.nev || user?.username}! Intézményi kulcs- és teremkezelési állapot.
          </p>
        </div>
      </div>

      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <span className="spinner spinner-primary" style={{ width: 24, height: 24 }} />
        </div>
      ) : error ? (
        <div className="alert alert-error" role="alert" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
          <span>{error}</span>
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => void loadDashboard()}>
            <RefreshCw size={14} strokeWidth={1.5} />
            <span>Újrapróbálás</span>
          </button>
        </div>
      ) : stats ? (
        <>
          <div className="stats-grid">
            <div className="stat-card">
              <div className="stat-card-icon">
                <DoorOpen size={18} strokeWidth={1.5} />
              </div>
              <div className="stat-card-body">
                <div className="stat-card-value">{stats.rooms}</div>
                <div className="stat-card-label">Regisztrált termek</div>
              </div>
            </div>

            <div className="stat-card">
              <div className="stat-card-icon">
                <Key size={18} strokeWidth={1.5} />
              </div>
              <div className="stat-card-body">
                <div className="stat-card-value">{stats.activeKeys}</div>
                <div className="stat-card-label">Jelenleg kiadott kulcsok</div>
              </div>
            </div>

            <div className="stat-card">
              <div className="stat-card-icon">
                <CalendarDays size={18} strokeWidth={1.5} />
              </div>
              <div className="stat-card-body">
                <div className="stat-card-value">{stats.todayBookings}</div>
                <div className="stat-card-label">Mai teremfoglalások</div>
              </div>
            </div>

            <div className="stat-card">
              <div className="stat-card-icon">
                <FileText size={18} strokeWidth={1.5} />
              </div>
              <div className="stat-card-body">
                <div className="stat-card-value">{stats.openTickets}</div>
                <div className="stat-card-label">Nyitott hibajegyek</div>
              </div>
            </div>
          </div>

          {/* Gyorshivatkozások */}
          <div className="card" style={{ marginTop: 24 }}>
            <h2 style={{ fontSize: 15, fontWeight: 600, color: 'var(--color-gray-900)', marginBottom: 16 }}>
              Kiemelt adminisztrációs modulok
            </h2>
            <div className="admin-quicklinks-grid">
              <Link to="/rooms" className="admin-quicklink-card">
                <div className="admin-quicklink-icon">
                  <DoorOpen size={18} strokeWidth={1.5} />
                </div>
                <div>
                  <div className="admin-quicklink-title">Termek kezelése</div>
                  <div className="admin-quicklink-desc">Tantermi adatok, kapacitások és elérhetőség</div>
                </div>
                <ArrowRight size={14} className="admin-quicklink-arrow" />
              </Link>

              <Link to="/keys" className="admin-quicklink-card">
                <div className="admin-quicklink-icon">
                  <Key size={18} strokeWidth={1.5} />
                </div>
                <div>
                  <div className="admin-quicklink-title">Kulcsnyilvántartás</div>
                  <div className="admin-quicklink-desc">Fizikai kulcsok és mesterkulcs-hozzáférések</div>
                </div>
                <ArrowRight size={14} className="admin-quicklink-arrow" />
              </Link>

              <Link to="/bookings" className="admin-quicklink-card">
                <div className="admin-quicklink-icon">
                  <CalendarDays size={18} strokeWidth={1.5} />
                </div>
                <div>
                  <div className="admin-quicklink-title">Összes foglalás</div>
                  <div className="admin-quicklink-desc">Oktatói idősávok és teremterhelés ellenőrzése</div>
                </div>
                <ArrowRight size={14} className="admin-quicklink-arrow" />
              </Link>

              <Link to="/reports" className="admin-quicklink-card">
                <div className="admin-quicklink-icon">
                  <BarChart2 size={18} strokeWidth={1.5} />
                </div>
                <div>
                  <div className="admin-quicklink-title">Riportok és statisztikák</div>
                  <div className="admin-quicklink-desc">Napi kulcsforgalom, kihasználtság és megbízhatóság</div>
                </div>
                <ArrowRight size={14} className="admin-quicklink-arrow" />
              </Link>
            </div>
          </div>
        </>
      ) : (
        <div className="alert alert-error" role="alert">Az áttekintő adatok nem érhetők el.</div>
      )}

      <style>{`
        .admin-quicklinks-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
          gap: 12px;
        }

        .admin-quicklink-card {
          display: flex;
          align-items: center;
          gap: 14px;
          padding: 14px;
          background-color: var(--color-gray-50);
          border: 1px solid var(--color-gray-200);
          border-radius: var(--border-radius-sm);
          text-decoration: none;
          color: inherit;
          transition: all 0.15s ease;
        }

        .admin-quicklink-card:hover {
          background-color: var(--color-primary-light);
          border-color: var(--color-primary-border);
          transform: translateY(-1px);
        }

        .admin-quicklink-icon {
          width: 36px;
          height: 36px;
          border-radius: var(--border-radius-sm);
          background-color: var(--color-white);
          border: 1px solid var(--color-gray-200);
          color: var(--color-primary);
          display: flex;
          align-items: center;
          justify-content: center;
          flex-shrink: 0;
        }

        .admin-quicklink-title {
          font-size: 13.5px;
          font-weight: 600;
          color: var(--color-gray-900);
        }

        .admin-quicklink-desc {
          font-size: 11.5px;
          color: var(--color-gray-500);
          margin-top: 2px;
        }

        .admin-quicklink-arrow {
          margin-left: auto;
          color: var(--color-gray-400);
          flex-shrink: 0;
        }

        .admin-quicklink-card:hover .admin-quicklink-arrow {
          color: var(--color-primary-hover);
        }
      `}</style>
    </div>
  )
}
