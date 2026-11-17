import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { AlertCircle, Building2, Key, Pencil, Plus, Search, Trash2, UserPlus, X } from 'lucide-react'
import client from '../../api/client'

interface KeyItem {
  id: number
  szam: string
  tipus: string
  teremId: number
  teremNev: string
  jelenlegiAllapot: string
  aktivalva: boolean
}

interface RoomOption {
  id: number
  nev: string
}

interface UserOption {
  id: number
  nev: string
  username: string
}

interface MasterAccess {
  userId: number
  nev: string
  createdAt: string
}

export default function KeysPage() {
  const [keys, setKeys] = useState<KeyItem[]>([])
  const [rooms, setRooms] = useState<RoomOption[]>([])
  const [search, setSearch] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [showEditor, setShowEditor] = useState(false)
  const [editingKey, setEditingKey] = useState<KeyItem | null>(null)
  const [keyNumber, setKeyNumber] = useState('')
  const [keyType, setKeyType] = useState('Osztalykulcs')
  const [roomId, setRoomId] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [masterKey, setMasterKey] = useState<KeyItem | null>(null)
  const [masterRoomKey, setMasterRoomKey] = useState<KeyItem | null>(null)
  const [masterRoomIds, setMasterRoomIds] = useState<number[]>([])
  const [isSavingMasterRooms, setIsSavingMasterRooms] = useState(false)
  const [masterAccess, setMasterAccess] = useState<MasterAccess[]>([])
  const [instructors, setInstructors] = useState<UserOption[]>([])
  const [selectedInstructorId, setSelectedInstructorId] = useState('')

  const fetchCatalog = useCallback(async () => {
    const [keyResponse, roomResponse] = await Promise.all([
      client.get<KeyItem[]>('/keys'),
      client.get<RoomOption[]>('/rooms'),
    ])
    return { keys: keyResponse.data, rooms: roomResponse.data }
  }, [])

  const loadCatalog = useCallback(async () => {
    const catalog = await fetchCatalog()
    setKeys(catalog.keys)
    setRooms(catalog.rooms)
  }, [fetchCatalog])

  useEffect(() => {
    let ignore = false
    fetchCatalog()
      .then((catalog) => {
        if (!ignore) {
          setKeys(catalog.keys)
          setRooms(catalog.rooms)
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const axErr = err as { response?: { data?: { message?: string } } }
          setError(axErr.response?.data?.message || 'Nem sikerült betölteni a kulcsokat.')
        }
      })
      .finally(() => {
        if (!ignore) setIsLoading(false)
      })
    return () => {
      ignore = true
    }
  }, [fetchCatalog])

  const openCreate = () => {
    setEditingKey(null)
    setKeyNumber('')
    setKeyType('Osztalykulcs')
    setRoomId(String(rooms[0]?.id ?? ''))
    setShowEditor(true)
  }

  const openEdit = (key: KeyItem) => {
    setEditingKey(key)
    setKeyNumber(key.szam)
    setKeyType(key.tipus)
    setRoomId(String(key.teremId))
    setShowEditor(true)
  }

  const saveKey = async (event: FormEvent) => {
    event.preventDefault()
    setIsSaving(true)
    try {
      const payload = {
        szam: keyNumber.trim(),
        tipus: keyType,
        teremId: Number(roomId),
        aktivalva: editingKey?.aktivalva ?? true,
      }
      if (editingKey) {
        await client.put(`/keys/${editingKey.id}`, payload)
      } else {
        await client.post('/keys', payload)
      }
      await loadCatalog()
      setShowEditor(false)
      setError('')
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A kulcs mentése sikertelen.')
    } finally {
      setIsSaving(false)
    }
  }

  const deactivateKey = async (key: KeyItem) => {
    if (!window.confirm(`Deaktiválod a(z) ${key.szam} kulcsot?`)) return
    try {
      await client.delete(`/keys/${key.id}`)
      setKeys((current) => current.map((item) =>
        item.id === key.id ? { ...item, aktivalva: false, jelenlegiAllapot: 'Deaktivalt' } : item))
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A kulcs deaktiválása sikertelen.')
    }
  }

  const loadMasterAccess = async (key: KeyItem) => {
    const [accessResponse, userResponse] = await Promise.all([
      client.get<MasterAccess[]>(`/keys/${key.id}/master-access`),
      client.get<{ items?: UserOption[] } | UserOption[]>('/users', {
        params: { role: 'Oktato', page: 1, pageSize: 100 },
      }),
    ])
    setMasterAccess(accessResponse.data)
    setInstructors(Array.isArray(userResponse.data) ? userResponse.data : (userResponse.data.items ?? []))
    setSelectedInstructorId('')
  }

  const openMasterAccess = async (key: KeyItem) => {
    setMasterKey(key)
    try {
      await loadMasterAccess(key)
      setError('')
    } catch (err: unknown) {
      setMasterKey(null)
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A mesterkulcs-jogosultságok nem tölthetők be.')
    }
  }

  const grantMasterAccess = async () => {
    if (!masterKey || !selectedInstructorId) return
    try {
      await client.post(`/keys/${masterKey.id}/master-access`, { userId: Number(selectedInstructorId) })
      await loadMasterAccess(masterKey)
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A mesterkulcs-jogosultság megadása sikertelen.')
    }
  }

  const revokeMasterAccess = async (userId: number) => {
    if (!masterKey) return
    try {
      await client.delete(`/keys/${masterKey.id}/master-access/${userId}`)
      setMasterAccess((current) => current.filter((access) => access.userId !== userId))
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A mesterkulcs-jogosultság visszavonása sikertelen.')
    }
  }

  const openMasterRooms = async (key: KeyItem) => {
    try {
      const response = await client.get<RoomOption[]>(`/keys/${key.id}/rooms`)
      setMasterRoomKey(key)
      setMasterRoomIds(response.data.map((room) => room.id))
      setError('')
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A mesterkulcs terem-hozzárendelései nem tölthetők be.')
    }
  }

  const saveMasterRooms = async () => {
    if (!masterRoomKey) return
    setIsSavingMasterRooms(true)
    try {
      await client.put(`/keys/${masterRoomKey.id}/rooms`, { teremIds: masterRoomIds })
      setMasterRoomKey(null)
      setError('')
    } catch (err: unknown) {
      const axErr = err as { response?: { data?: { message?: string } } }
      setError(axErr.response?.data?.message || 'A mesterkulcs termeinek mentése sikertelen.')
    } finally {
      setIsSavingMasterRooms(false)
    }
  }

  const filtered = keys.filter((key) => {
    const query = search.toLowerCase()
    return key.szam.toLowerCase().includes(query) || key.teremNev.toLowerCase().includes(query)
  })

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <Key size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Kulcsok nyilvántartása</span>
          </h1>
          <p className="page-subtitle">Terem- és mesterkulcsok, illetve mesterkulcs-jogosultságok kezelése</p>
        </div>
        <div className="search-bar">
          <Search size={15} color="var(--color-gray-400)" strokeWidth={1.5} />
          <input
            type="text"
            placeholder="Keresés kulcsszám vagy terem szerint…"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>
        <button type="button" className="btn btn-primary" onClick={openCreate} disabled={rooms.length === 0}>
          <Plus size={15} /> <span>Új kulcs</span>
        </button>
      </div>

      {error && (
        <div className="alert alert-error">
          <AlertCircle size={16} strokeWidth={1.5} style={{ flexShrink: 0 }} />
          <span>{error}</span>
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
                <th>Kulcsszám</th>
                <th>Típus</th>
                <th>Hozzárendelt terem</th>
                <th>Jelenlegi státusz</th>
                <th>Engedélyezve</th>
                <th>Művelet</th>
              </tr>
            </thead>
            <tbody>
              {filtered.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', color: 'var(--color-gray-500)', padding: 36 }}>
                    Nincs a keresésnek megfelelő kulcs.
                  </td>
                </tr>
              ) : filtered.map((key) => (
                <tr key={key.id}>
                  <td style={{ fontWeight: 600, color: 'var(--color-gray-900)' }}>Kulcs #{key.szam}</td>
                  <td><span className="badge badge-neutral">{key.tipus}</span></td>
                  <td>{key.teremNev}</td>
                  <td>
                    {key.jelenlegiAllapot === 'Deaktivalt' ? (
                      <span className="badge badge-danger">Deaktivált</span>
                    ) : key.jelenlegiAllapot === 'Kiadva' ? (
                      <span className="badge badge-warning">Kiadva</span>
                    ) : (
                      <span className="badge badge-success">Szabadon elérhető</span>
                    )}
                  </td>
                  <td>{key.aktivalva ? 'Igen' : 'Nem'}</td>
                  <td>
                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 6 }}>
                      {key.tipus === 'Mesterkulcs' && (
                        <>
                          <button type="button" className="btn btn-secondary btn-sm" onClick={() => openMasterRooms(key)}>
                            <Building2 size={14} /> Termek
                          </button>
                          <button type="button" className="btn btn-secondary btn-sm" onClick={() => openMasterAccess(key)}>
                            Jogosultságok
                          </button>
                        </>
                      )}
                      <button type="button" className="btn btn-ghost btn-sm" onClick={() => openEdit(key)} title="Szerkesztés">
                        <Pencil size={14} />
                      </button>
                      {key.aktivalva && (
                        <button type="button" className="btn btn-ghost btn-sm" onClick={() => deactivateKey(key)} title="Deaktiválás">
                          <Trash2 size={14} />
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showEditor && (
        <div className="modal-overlay" onClick={(event) => { if (event.target === event.currentTarget) setShowEditor(false) }}>
          <form className="modal" style={{ maxWidth: 480 }} onSubmit={saveKey}>
            <div className="modal-header">
              <h2 className="modal-title">{editingKey ? 'Kulcs szerkesztése' : 'Új kulcs rögzítése'}</h2>
              <button type="button" className="modal-close" onClick={() => setShowEditor(false)} aria-label="Bezárás">
                <X size={18} />
              </button>
            </div>
            <div className="form-group">
              <label className="form-label" htmlFor="key-number">Kulcsszám *</label>
              <input id="key-number" className="form-input" maxLength={20} value={keyNumber}
                onChange={(event) => setKeyNumber(event.target.value)} required />
            </div>
            <div className="form-group">
              <label className="form-label" htmlFor="key-type">Típus *</label>
              <select id="key-type" className="form-select" value={keyType} onChange={(event) => setKeyType(event.target.value)}>
                <option value="Osztalykulcs">Osztálykulcs</option>
                <option value="Mesterkulcs">Mesterkulcs</option>
              </select>
            </div>
            <div className="form-group">
              <label className="form-label" htmlFor="key-room">Terem *</label>
              <select id="key-room" className="form-select" value={roomId} onChange={(event) => setRoomId(event.target.value)} required>
                {rooms.map((room) => <option key={room.id} value={room.id}>{room.nev}</option>)}
              </select>
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
              <button type="button" className="btn btn-secondary" onClick={() => setShowEditor(false)}>Mégse</button>
              <button type="submit" className="btn btn-primary" disabled={isSaving}>
                {isSaving ? 'Mentés…' : 'Mentés'}
              </button>
            </div>
          </form>
        </div>
      )}

      {masterKey && (
        <div className="modal-overlay" onClick={(event) => { if (event.target === event.currentTarget) setMasterKey(null) }}>
          <div className="modal" style={{ maxWidth: 560 }}>
            <div className="modal-header">
              <div>
                <h2 className="modal-title">Mesterkulcs-jogosultságok</h2>
                <p className="page-subtitle">Kulcs #{masterKey.szam}</p>
              </div>
              <button type="button" className="modal-close" onClick={() => setMasterKey(null)} aria-label="Bezárás">
                <X size={18} />
              </button>
            </div>
            <div className="form-group">
              <label className="form-label" htmlFor="master-recipient">Jogosult oktató hozzáadása</label>
              <div style={{ display: 'flex', gap: 8 }}>
                <select id="master-recipient" className="form-select" value={selectedInstructorId}
                  onChange={(event) => setSelectedInstructorId(event.target.value)}>
                  <option value="">Válassz oktatót</option>
                  {instructors.filter((user) => !masterAccess.some((access) => access.userId === user.id)).map((user) => (
                    <option key={user.id} value={user.id}>{user.nev} ({user.username})</option>
                  ))}
                </select>
                <button type="button" className="btn btn-primary" onClick={grantMasterAccess}
                  disabled={!selectedInstructorId} aria-label="Jogosultság megadása">
                  <UserPlus size={16} />
                </button>
              </div>
            </div>
            <ul style={{ paddingLeft: 20 }}>
              {masterAccess.map((access) => (
                <li key={access.userId} style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0' }}>
                  <span>{access.nev}</span>
                  <button type="button" className="btn btn-ghost btn-sm" onClick={() => revokeMasterAccess(access.userId)}>
                    Visszavonás
                  </button>
                </li>
              ))}
              {masterAccess.length === 0 && <li>Nincs kiosztott jogosultság.</li>}
            </ul>
          </div>
        </div>
      )}

      {masterRoomKey && (
        <div className="modal-overlay" onClick={(event) => { if (event.target === event.currentTarget) setMasterRoomKey(null) }}>
          <div className="modal" style={{ maxWidth: 560 }}>
            <div className="modal-header">
              <div>
                <h2 className="modal-title">Mesterkulcs termei</h2>
                <p className="page-subtitle">Kulcs #{masterRoomKey.szam}</p>
              </div>
              <button type="button" className="modal-close" onClick={() => setMasterRoomKey(null)} aria-label="Bezárás">
                <X size={18} />
              </button>
            </div>
            <div className="form-group">
              <p className="form-label">A kulccsal elérhető termek</p>
              {rooms.map((room) => (
                <label key={room.id} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '6px 0' }}>
                  <input
                    type="checkbox"
                    checked={masterRoomIds.includes(room.id)}
                    onChange={(event) => setMasterRoomIds((current) => event.target.checked
                      ? [...current, room.id]
                      : current.filter((id) => id !== room.id))}
                  />
                  <span>{room.nev}</span>
                </label>
              ))}
              {rooms.length === 0 && <p className="form-hint">Nincs hozzárendelhető terem.</p>}
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
              <button type="button" className="btn btn-secondary" onClick={() => setMasterRoomKey(null)}>Mégse</button>
              <button type="button" className="btn btn-primary" onClick={saveMasterRooms}
                disabled={isSavingMasterRooms || rooms.length === 0}>
                {isSavingMasterRooms ? 'Mentés…' : 'Mentés'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
