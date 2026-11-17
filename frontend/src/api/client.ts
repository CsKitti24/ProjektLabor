import axios from 'axios'

// A bejelentkezési token HttpOnly sütiben utazik (a backend állítja be), ezért a kliens nem
// tárolja és nem küldi kézzel; az azonos originű /api kérésekhez a böngésző csatolja.
const client = axios.create({
  baseURL: '/api',
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
})

// Régebbi verzió által a localStorage-ban hagyott adatok törlése.
const LEGACY_STORAGE_KEYS = ['auth_token', 'user_role', 'username', 'user_id', 'user_nev', 'user_email']
try {
  LEGACY_STORAGE_KEYS.forEach((key) => localStorage.removeItem(key))
} catch {
  // A tároló nem elérhető (pl. privát mód) – nincs mit törölni.
}

client.interceptors.response.use(
  (response) => response,
  (error) => {
    const url: string = error.config?.url ?? ''
    // Lejárt vagy érvénytelen munkamenet: vissza a kezdőoldalra. A bejelentkezési kísérlet és a
    // munkamenet-ellenőrzés (/auth/me) 401-e nem ok az átirányításra.
    if (
      error.response?.status === 401 &&
      !url.includes('/auth/login') &&
      !url.includes('/auth/me') &&
      window.location.pathname !== '/'
    ) {
      window.location.href = '/'
    }
    return Promise.reject(error)
  }
)

export default client
