import { useState, type FormEvent } from 'react'
import { AlertCircle, UserPlus, X } from 'lucide-react'
import client from '../../api/client'
import DialogKeyboard from '../../components/DialogKeyboard'

const ROLE_OPTIONS = [
  { value: 'Oktato', label: 'Oktató' },
  { value: 'Portas', label: 'Portás' },
  { value: 'Adminisztrator', label: 'Adminisztrátor' },
  { value: 'UzemeltetesiIgazgato', label: 'Üzemeltetési igazgató' },
]

const USERNAME_PATTERN = /^[A-Za-z0-9._-]{3,50}$/

interface CreateUserModalProps {
  onClose: () => void
  onCreated: (message: string) => void
}

function errorMessage(err: unknown, fallback: string): string {
  const axErr = err as { response?: { data?: { message?: string; errors?: Record<string, string[]> } } }
  const data = axErr.response?.data
  const firstValidationError = data?.errors ? Object.values(data.errors).flat()[0] : undefined
  return data?.message || firstValidationError || fallback
}

export default function CreateUserModal({ onClose, onCreated }: CreateUserModalProps) {
  const [nev, setNev] = useState('')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [role, setRole] = useState('Oktato')
  const [password, setPassword] = useState('')
  const [passwordConfirmation, setPasswordConfirmation] = useState('')
  const [pin, setPin] = useState('')
  const [error, setError] = useState('')
  const [isSaving, setIsSaving] = useState(false)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError('')

    if (!nev.trim() || !email.trim()) {
      setError('A név és az e-mail cím megadása kötelező.')
      return
    }
    if (!USERNAME_PATTERN.test(username.trim())) {
      setError('A felhasználónév 3–50 karakter: angol betűk, számjegyek, pont, kötőjel vagy aláhúzás.')
      return
    }
    if (password.length < 12) {
      setError('A jelszónak legalább 12 karakterből kell állnia.')
      return
    }
    if (password !== passwordConfirmation) {
      setError('A két jelszó nem egyezik.')
      return
    }
    if (role === 'Oktato' && pin && !/^[0-9]{4,8}$/.test(pin)) {
      setError('A PIN-kód 4–8 számjegyből állhat.')
      return
    }

    setIsSaving(true)
    try {
      const response = await client.post<{ id: number }>('/users', {
        nev: nev.trim(),
        username: username.trim(),
        email: email.trim(),
        role,
        jelszo: password,
      })
      let message = `${nev.trim()} felhasználó létrehozva.`
      if (role === 'Oktato' && pin) {
        try {
          await client.put(`/users/${response.data.id}/pin`, { pin })
        } catch {
          message += ' A PIN-kód beállítása nem sikerült, a felhasználó szerkesztésénél pótolható.'
        }
      }
      onCreated(message)
    } catch (err: unknown) {
      setError(errorMessage(err, 'A felhasználó létrehozása nem sikerült.'))
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <DialogKeyboard onClose={() => onClose()} />
      <div className="modal" style={{ maxWidth: 480 }} role="dialog" aria-modal="true" aria-labelledby="create-user-title">
        <div className="modal-header">
          <div>
            <h2 className="modal-title" id="create-user-title">Új felhasználó</h2>
            <p style={{ fontSize: 12.5, color: 'var(--color-gray-500)', marginTop: 2 }}>
              A felhasználó az első belépés után a profiljában cserélheti a jelszavát.
            </p>
          </div>
          <button type="button" className="modal-close" onClick={onClose} aria-label="Bezárás">
            <X size={18} strokeWidth={1.5} />
          </button>
        </div>

        {error && (
          <div className="alert alert-error" role="alert">
            <AlertCircle size={15} strokeWidth={1.5} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={(event) => void handleSubmit(event)} noValidate>
          <div className="form-group">
            <label className="form-label" htmlFor="new-user-name">Teljes név *</label>
            <input id="new-user-name" className="form-input" value={nev} maxLength={100}
              onChange={(e) => setNev(e.target.value)} required />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="new-user-username">Felhasználónév *</label>
            <input id="new-user-username" className="form-input" value={username} maxLength={50}
              autoComplete="off" onChange={(e) => setUsername(e.target.value)} required />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="new-user-email">E-mail cím *</label>
            <input id="new-user-email" className="form-input" type="email" value={email} maxLength={100}
              onChange={(e) => setEmail(e.target.value)} required />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="new-user-role">Szerepkör *</label>
            <select id="new-user-role" className="form-select" value={role} onChange={(e) => setRole(e.target.value)}>
              {ROLE_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
            </select>
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="new-user-password">Kezdő jelszó (legalább 12 karakter) *</label>
            <input id="new-user-password" className="form-input" type="password" value={password}
              autoComplete="new-password" onChange={(e) => setPassword(e.target.value)} required />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="new-user-password-confirm">Jelszó megerősítése *</label>
            <input id="new-user-password-confirm" className="form-input" type="password" value={passwordConfirmation}
              autoComplete="new-password" onChange={(e) => setPasswordConfirmation(e.target.value)} required />
          </div>
          {role === 'Oktato' && (
            <div className="form-group">
              <label className="form-label" htmlFor="new-user-pin">Kulcsátvételi PIN (4–8 számjegy, később is megadható)</label>
              <input id="new-user-pin" className="form-input" type="password" inputMode="numeric" maxLength={8}
                value={pin} autoComplete="new-password" onChange={(e) => setPin(e.target.value)} />
            </div>
          )}

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 16 }}>
            <button type="button" className="btn btn-secondary" onClick={onClose}>Mégse</button>
            <button type="submit" className="btn btn-primary" disabled={isSaving}>
              {isSaving ? <span className="spinner" /> : <UserPlus size={15} strokeWidth={1.5} />}
              <span>Létrehozás</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
