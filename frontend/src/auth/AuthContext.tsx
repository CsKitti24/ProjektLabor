import React, { createContext, useContext, useState, useCallback, useEffect } from 'react'
import client from '../api/client'

export type UserRole = 'Oktato' | 'Portas' | 'Adminisztrator' | 'UzemeltetesiIgazgato'

export interface User {
  userId: string | number
  username: string
  nev?: string
  email?: string
  role: UserRole
}

interface AuthContextType {
  user: User | null
  token: string | null
  login: (username: string, password: string) => Promise<void>
  logout: () => void
  isLoading: boolean
}

const AuthContext = createContext<AuthContextType | null>(null)

interface BackendLoginResponse {
  token?: string
  Token?: string
  expiresAt?: string
  felhasznalo?: {
    id: number | string
    username: string
    nev?: string
    email?: string
    role: UserRole
  }
  // Alternatív lapos struktúra esetére:
  userId?: string | number
  username?: string
  role?: UserRole
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(() => {
    const storedRole = localStorage.getItem('user_role') as UserRole | null
    const storedUsername = localStorage.getItem('username')
    const storedUserId = localStorage.getItem('user_id')
    const storedNev = localStorage.getItem('user_nev') ?? undefined
    const storedEmail = localStorage.getItem('user_email') ?? undefined
    const storedToken = localStorage.getItem('auth_token')

    if (storedToken && storedRole && storedUsername) {
      return {
        userId: storedUserId ?? '',
        username: storedUsername,
        nev: storedNev,
        email: storedEmail,
        role: storedRole,
      }
    }
    return null
  })

  const [token, setToken] = useState<string | null>(() => localStorage.getItem('auth_token'))
  const [isLoading, setIsLoading] = useState<boolean>(true)

  // Kezdeti felhasználó adategyeztetés a backenddel /api/auth/me végponton
  useEffect(() => {
    const currentToken = localStorage.getItem('auth_token')
    if (!currentToken) {
      setIsLoading(false)
      return
    }

    client
      .get<{ id: number; username: string; nev: string; email: string; role: UserRole }>('/auth/me')
      .then((res) => {
        if (res.data && res.data.username) {
          const freshUser: User = {
            userId: res.data.id,
            username: res.data.username,
            nev: res.data.nev,
            email: res.data.email,
            role: res.data.role,
          }
          setUser(freshUser)
          localStorage.setItem('user_role', freshUser.role)
          localStorage.setItem('username', freshUser.username)
          localStorage.setItem('user_id', String(freshUser.userId))
          if (freshUser.nev) localStorage.setItem('user_nev', freshUser.nev)
          if (freshUser.email) localStorage.setItem('user_email', freshUser.email)
        }
      })
      .catch(() => {
        // Ha a token lejárt, a client interceptor kezeli a kijelentkeztetést
      })
      .finally(() => {
        setIsLoading(false)
      })
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem('auth_token')
    localStorage.removeItem('user_role')
    localStorage.removeItem('username')
    localStorage.removeItem('user_id')
    localStorage.removeItem('user_nev')
    localStorage.removeItem('user_email')
    setToken(null)
    setUser(null)
    client.post('/auth/logout').catch(() => {})
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    // A C# backend DTO a { Username, Jelszo } mezőket várja, de küldjük el mindkét variációt
    const payload = {
      username: username.trim(),
      jelszo: password,
      password: password,
    }

    const response = await client.post<BackendLoginResponse>('/auth/login', payload)
    const data = response.data

    const jwtToken = data.token || data.Token || ''
    const userInfo = data.felhasznalo

    const resolvedRole: UserRole = (userInfo?.role || data.role || 'Oktato') as UserRole
    const resolvedUsername: string = userInfo?.username || data.username || username.trim()
    const resolvedUserId: string | number = userInfo?.id ?? data.userId ?? ''
    const resolvedNev: string = userInfo?.nev || ''
    const resolvedEmail: string = userInfo?.email || ''

    if (jwtToken) {
      localStorage.setItem('auth_token', jwtToken)
      setToken(jwtToken)
    }

    localStorage.setItem('user_role', resolvedRole)
    localStorage.setItem('username', resolvedUsername)
    localStorage.setItem('user_id', String(resolvedUserId))
    if (resolvedNev) localStorage.setItem('user_nev', resolvedNev)
    if (resolvedEmail) localStorage.setItem('user_email', resolvedEmail)

    setUser({
      userId: resolvedUserId,
      username: resolvedUsername,
      nev: resolvedNev,
      email: resolvedEmail,
      role: resolvedRole,
    })
  }, [])

  return (
    <AuthContext.Provider value={{ user, token, login, logout, isLoading }}>
      {children}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextType {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
