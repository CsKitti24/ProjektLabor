import axios from 'axios'

const client = axios.create({
  baseURL: '/api',
  headers: {
    'Content-Type': 'application/json',
  },
})

client.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

client.interceptors.response.use(
  (response) => response,
  (error) => {
    // Csak akkor irányítunk át a főoldalra, ha NEM a bejelentkezési kísérlet kapott 401-et
    if (
      error.response?.status === 401 &&
      !error.config?.url?.includes('/auth/login')
    ) {
      localStorage.removeItem('auth_token')
      localStorage.removeItem('user_role')
      localStorage.removeItem('username')
      localStorage.removeItem('user_id')
      localStorage.removeItem('user_nev')
      localStorage.removeItem('user_email')
      window.location.href = '/'
    }
    return Promise.reject(error)
  }
)

export default client
