import type { ReactNode } from 'react'

export const Icon = {
  Shield: (p: { size?: number }) => (
    <svg width={p.size ?? 20} height={p.size ?? 20} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 3l8 3v6c0 4.5-3.2 8.2-8 9-4.8-.8-8-4.5-8-9V6l8-3z" /><path d="M9 12l2 2 4-4" />
    </svg>
  ),
  Mail: () => (
    <svg className="lead" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="3" y="5" width="18" height="14" rx="2" /><path d="M3 7l9 6 9-6" />
    </svg>
  ),
  Lock: () => (
    <svg className="lead" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="4" y="11" width="16" height="10" rx="2" /><path d="M8 11V8a4 4 0 018 0v3" />
    </svg>
  ),
  Phone: (p: { lead?: boolean; size?: number }) => (
    <svg className={p.lead ? 'lead' : undefined} width={p.size ?? 18} height={p.size ?? 18} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="7" y="2" width="10" height="20" rx="2" /><path d="M11 18h2" />
    </svg>
  ),
  Eye: ({ off }: { off: boolean }) => (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" />
      {off && <path d="M4 4l16 16" />}
    </svg>
  ),
  Alert: () => (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ flexShrink: 0, marginTop: 1 }}>
      <circle cx="12" cy="12" r="9" /><path d="M12 8v5M12 16h.01" />
    </svg>
  ),
  Check: () => (
    <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M5 13l4 4L19 7" /></svg>
  ),
}

interface Props {
  title: string
  subtitle?: string
  /** 1 = password step, 2 = verification step. Omit to hide the stepper. */
  step?: 1 | 2
  children: ReactNode
  footer?: ReactNode
}

export function AuthShell({ title, subtitle, step, children, footer }: Props) {
  return (
    <div className="auth-page">
      <div className="grid-overlay" />
      <div className="blob" />
      <section className="auth-card" aria-labelledby="auth-title">
        <div className="brand">
          <span className="brand-mark"><Icon.Shield /></span>
          SecureGate
        </div>

        {step && (
          <ol className="steps" aria-label="Sign-in progress" style={{ listStyle: 'none', padding: 0 }}>
            <li className={`step ${step > 1 ? 'done' : 'active'}`}>
              <span className="dot">{step > 1 ? '✓' : '1'}</span>Password
            </li>
            <span className={`step-line ${step > 1 ? 'done' : ''}`} />
            <li className={`step ${step === 2 ? 'active' : ''}`}>
              <span className="dot">2</span>Verify code
            </li>
          </ol>
        )}

        <h1 id="auth-title">{title}</h1>
        {subtitle && <p className="subtitle">{subtitle}</p>}
        {children}
        {footer && <div className="footer-link">{footer}</div>}

        <div className="secure-note">
          <Icon.Lock /> <span style={{ marginLeft: -4 }}>Protected with two-step verification</span>
        </div>
      </section>
    </div>
  )
}

export function ErrorAlert({ message }: { message: string | null }) {
  if (!message) return null
  return (
    <div role="alert" className="alert">
      <Icon.Alert />
      <span>{message}</span>
    </div>
  )
}

export function PasswordInput(props: {
  id: string
  value: string
  onChange: (v: string) => void
  autoComplete: string
  placeholder?: string
  minLength?: number
  show: boolean
  onToggle: () => void
}) {
  return (
    <div className="input-wrap">
      <Icon.Lock />
      <input
        id={props.id}
        className="input"
        type={props.show ? 'text' : 'password'}
        autoComplete={props.autoComplete}
        placeholder={props.placeholder}
        minLength={props.minLength}
        value={props.value}
        onChange={(e) => props.onChange(e.target.value)}
        required
      />
      <button type="button" className="toggle-pw" onClick={props.onToggle} aria-label={props.show ? 'Hide password' : 'Show password'}>
        <Icon.Eye off={props.show} />
      </button>
    </div>
  )
}
