import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export default function NotFoundPage() {
  const { user } = useAuth()

  return (
    <div style={{ maxWidth: 520, margin: '80px auto', padding: 24 }} className="card">
      <h1 style={{ fontSize: 18, fontWeight: 600, marginBottom: 8 }}>Az oldal nem található</h1>
      <p style={{ color: 'var(--color-gray-600)', marginBottom: 16 }}>
        A keresett cím nem létezik, vagy időközben megváltozott. Ellenőrizd a címet, vagy válassz a menüből.
      </p>
      <Link className="btn btn-primary" to={user ? '/profile' : '/'}>
        {user ? 'Vissza a profilomhoz' : 'Vissza a kezdőlapra'}
      </Link>
    </div>
  )
}
