import React, { createContext, useContext, useState, useCallback, useEffect } from 'react'
import client from '../api/client'
import SessionKeeper from './SessionKeeper'

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
  login: (username: string, password: string) => Promise<User>
  logout: () => void
  isLoading: boolean
}

const AuthContext = createContext<AuthContextType | null>(null)

interface BackendUser {
  id: number | string
  username: string
  nev?: string
  email?: string
  role: UserRole
}

interface BackendLoginResponse {
  felhasznalo?: BackendUser
}

function toUser(data: BackendUser): User {
  return {
    userId: data.id,
    username: data.username,
    nev: data.nev,
    email: data.email,
    role: data.role,
  }
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  // A munkamenetet a HttpOnly süti tárolja; a felhasználó adatait induláskor a /auth/me adja vissza.
  const [user, setUser] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)

  useEffect(() => {
    let ignore = false
    client
      .get<BackendUser>('/auth/me')
      .then((res) => {
        if (!ignore && res.data?.username && res.data.role) {
          setUser(toUser(res.data))
        }
      })
      .catch(() => {
        // Nincs érvényes munkamenet: a felhasználó nincs bejelentkezve.
      })
      .finally(() => {
        if (!ignore) setIsLoading(false)
      })
    return () => {
      ignore = true
    }
  }, [])

  const logout = useCallback(() => {
    setUser(null)
    client.post('/auth/logout').catch(() => {})
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    // A backend LoginRequestDto a { Username, Jelszo } mezőket várja; a tokent sütiben adja vissza.
    const response = await client.post<BackendLoginResponse>('/auth/login', {
      username: username.trim(),
      jelszo: password,
    })

    const userInfo = response.data?.felhasznalo
    if (!userInfo?.username || !userInfo.role) {
      // Hiányos válasz esetén nem találgatunk szerepkört: a bejelentkezés sikertelennek számít.
      throw new Error('A bejelentkezési válasz nem tartalmaz felhasználót vagy szerepkört.')
    }

    const loggedIn = toUser(userInfo)
    setUser(loggedIn)
    return loggedIn
  }, [])

  return (
    <AuthContext.Provider value={{ user, login, logout, isLoading }}>
      {children}
      {user && <SessionKeeper onExpired={logout} />}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextType {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
