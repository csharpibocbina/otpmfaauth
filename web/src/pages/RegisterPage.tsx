import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { AuthShell, ErrorAlert, Icon, PasswordInput } from '../components/AuthShell'

export function RegisterPage() {
  const nav = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPw, setShowPw] = useState(false)
  const [phone, setPhone] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await api.register(email, password, phone.trim() || undefined)
      nav('/login')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthShell
      title="Create your account"
      subtitle="Add a mobile number to protect sign-ins with a one-time code."
      footer={<>Already registered? <Link to="/login">Sign in</Link></>}
    >
      <form className="form" onSubmit={submit}>
        <div className="field">
          <label htmlFor="reg-email">Email</label>
          <div className="input-wrap">
            <Icon.Mail />
            <input id="reg-email" className="input" type="email" autoComplete="email" placeholder="you@example.com"
              value={email} onChange={(e) => setEmail(e.target.value)} required />
          </div>
        </div>
        <div className="field">
          <label htmlFor="reg-password">Password</label>
          <PasswordInput id="reg-password" value={password} onChange={setPassword} autoComplete="new-password"
            placeholder="At least 12 characters" minLength={12} show={showPw} onToggle={() => setShowPw((s) => !s)} />
        </div>
        <div className="field">
          <label htmlFor="reg-phone">Mobile number</label>
          <div className="input-wrap">
            <Icon.Phone lead />
            <input id="reg-phone" className="input" type="tel" autoComplete="tel" placeholder="+14155550100"
              pattern="\+[1-9][0-9]{7,14}" title="International format, e.g. +14155550100"
              value={phone} onChange={(e) => setPhone(e.target.value)} />
          </div>
        </div>
        <ErrorAlert message={error} />
        <button className="btn" disabled={busy}>
          {busy ? <><span className="spinner" /> Creating…</> : 'Create account'}
        </button>
      </form>
    </AuthShell>
  )
}
