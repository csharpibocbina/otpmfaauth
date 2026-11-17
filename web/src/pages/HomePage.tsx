import { useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api, type Profile } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { displayName, greeting, initials } from '../auth/displayName'
import { Icon } from '../components/AuthShell'

export function HomePage() {
  const { accessToken, signOut } = useAuth()
  const [profile, setProfile] = useState<Profile | null>(null)

  useEffect(() => {
    if (accessToken) api.me(accessToken).then(setProfile).catch(signOut)
  }, [accessToken, signOut])

  if (!accessToken) return <Navigate to="/login" replace />

  const name = displayName(profile?.email)
  const since = profile?.memberSince ? new Date(profile.memberSince).toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' }) : '—'

  return (
    <div className="auth-page">
      <div className="grid-overlay" />
      <div className="blob" />
      <section className="auth-card welcome-card" aria-labelledby="welcome-title">
        <div className="welcome-top">
          <div className="brand" style={{ marginBottom: 0 }}>
            <span className="brand-mark"><Icon.Shield /></span>
            SecureGate
          </div>
          <button className="btn btn-ghost btn-sm" onClick={signOut}>Sign out</button>
        </div>

        <div className="welcome-hero">
          <div className="avatar" aria-hidden="true">{profile ? initials(name) : ''}</div>
          <div>
            <p className="eyebrow">{greeting()}</p>
            <h1 id="welcome-title">Welcome, {profile ? name : '…'}!</h1>
            <p className="subtitle" style={{ margin: 0 }}>You're signed in securely with two-step verification.</p>
          </div>
        </div>

        <div className="chips">
          <span className="chip ok"><Icon.Shield size={14} /> Two-step verification {profile?.mfaEnabled ? 'on' : 'off'}</span>
          {profile?.phoneVerified && <span className="chip ok"><Icon.Phone size={14} /> Phone verified</span>}
        </div>

        <dl className="info">
          <div><dt>Email</dt><dd>{profile?.email ?? '—'}</dd></div>
          <div><dt>Mobile</dt><dd>{profile?.maskedPhone ?? 'Not added'}</dd></div>
          <div><dt>Member since</dt><dd>{since}</dd></div>
        </dl>
      </section>
    </div>
  )
}
