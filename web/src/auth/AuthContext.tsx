import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'

interface AuthState {
  /** Final access token. Kept in memory only (never localStorage). */
  accessToken: string | null
  /** Short-lived token proving the password step passed. */
  mfaToken: string | null
  setAccessToken: (t: string | null) => void
  setMfaToken: (t: string | null) => void
  signOut: () => void
}

const Ctx = createContext<AuthState | null>(null)

export function AuthProvider({ children, initialMfaToken = null }: { children: ReactNode; initialMfaToken?: string | null }) {
  const [accessToken, setAccessToken] = useState<string | null>(null)
  const [mfaToken, setMfaToken] = useState<string | null>(initialMfaToken)
  const signOut = useCallback(() => {
    setAccessToken(null)
    setMfaToken(null)
  }, [])
  const value = useMemo(
    () => ({ accessToken, mfaToken, setAccessToken, setMfaToken, signOut }),
    [accessToken, mfaToken, signOut],
  )
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useAuth() {
  const ctx = useContext(Ctx)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
