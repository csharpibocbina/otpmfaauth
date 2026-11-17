import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { AuthShell, ErrorAlert, Icon, PasswordInput } from '../components/AuthShell'

export function LoginPage() {
  const nav = useNavigate()
  const { setAccessToken, setMfaToken } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPw, setShowPw] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const res = await api.login(email, password)
      if (res.mfaRequired) {
        setMfaToken(res.mfaToken)
        nav('/mfa')
      } else {
        setAccessToken(res.accessToken)
        nav('/')
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthShell
      title="Welcome back"
      subtitle="Sign in to continue. We'll text you a code to confirm it's you."
      step={1}
      footer={<>New here? <Link to="/register">Create an account</Link></>}
    >
      <form className="form" onSubmit={submit}>
        <div className="field">
          <label htmlFor="email">Email</label>
          <div className="input-wrap">
            <Icon.Mail />
            <input id="email" className="input" type="email" autoComplete="username" placeholder="you@example.com"
              value={email} onChange={(e) => setEmail(e.target.value)} required />
          </div>
        </div>
        <div className="field">
          <label htmlFor="password">Password</label>
          <PasswordInput id="password" value={password} onChange={setPassword} autoComplete="current-password"
            placeholder="Your password" show={showPw} onToggle={() => setShowPw((s) => !s)} />
        </div>
        <ErrorAlert message={error} />
        <button className="btn" disabled={busy}>
          {busy ? <><span className="spinner" /> Signing in…</> : 'Continue'}
        </button>
      </form>
    </AuthShell>
  )
}
