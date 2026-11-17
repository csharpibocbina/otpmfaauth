import { useEffect, useRef, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { api, ApiError, type ChallengeResponse } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { AuthShell, ErrorAlert, Icon } from '../components/AuthShell'
import { OtpInput } from '../components/OtpInput'
import { useNotifications } from '../realtime/useNotifications'

type Stage = 'requesting' | 'sending' | 'enterCode' | 'verifying' | 'failed'

const RESEND_COOLDOWN_S = 30

export function MfaChallengePage() {
  const nav = useNavigate()
  const { mfaToken, accessToken, setAccessToken, setMfaToken } = useAuth()
  const [stage, setStage] = useState<Stage>('requesting')
  const [challenge, setChallenge] = useState<ChallengeResponse | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [cooldown, setCooldown] = useState(0)
  const started = useRef(false)

  // SignalR tells us when the worker has actually handed the code to Vonage.
  useNotifications(mfaToken, {
    OtpSent: (p) => { if (p.challengeId === challenge?.challengeId) setStage('enterCode') },
    OtpDeliveryFailed: (p) => {
      if (p.challengeId !== challenge?.challengeId) return
      setError('We could not send a code to your phone. Please try again.')
      setStage('failed')
    },
  })

  async function requestCode() {
    if (!mfaToken) return
    setError(null)
    setCode('')
    setStage('requesting')
    try {
      const c = await api.startChallenge(mfaToken)
      setChallenge(c)
      setStage('sending')
      setCooldown(RESEND_COOLDOWN_S)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not start verification.')
      setStage('failed')
    }
  }

  useEffect(() => {
    if (started.current) return
    started.current = true
    void requestCode()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (cooldown <= 0) return
    const t = setTimeout(() => setCooldown((c) => c - 1), 1000)
    return () => clearTimeout(t)
  }, [cooldown])

  // The SignalR event can be missed (reconnect, race): fall back to the code boxes after a few seconds.
  useEffect(() => {
    if (stage !== 'sending') return
    const t = setTimeout(() => setStage((s) => (s === 'sending' ? 'enterCode' : s)), 8000)
    return () => clearTimeout(t)
  }, [stage])

  async function submit(value: string) {
    if (!mfaToken || !challenge || value.length !== 6) return
    setStage('verifying')
    setError(null)
    try {
      const res = await api.verify(mfaToken, challenge.challengeId, value)
      setAccessToken(res.accessToken)
      setMfaToken(null)
      nav('/')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Verification failed.')
      setCode('')
      setStage(e instanceof ApiError && (e.code === 'ChallengeLocked' || e.code === 'ChallengeExpired') ? 'failed' : 'enterCode')
    }
  }

  // Once verified the MFA token is cleared; send the user onward, not back to login.
  if (!mfaToken) return <Navigate to={accessToken ? '/' : '/login'} replace />

  const waiting = stage === 'requesting' || stage === 'sending'
  const entering = stage === 'enterCode' || stage === 'verifying'

  return (
    <AuthShell
      title="Check your phone"
      subtitle={waiting ? 'Sending your verification code…' : 'Enter the 6-digit code to finish signing in.'}
      step={2}
    >
      {challenge && (
        <span className="phone-badge"><Icon.Phone /> {challenge.maskedDestination}</span>
      )}

      {waiting && (
        <div className="sending" role="status">
          <div className="pulse-ring"><Icon.Phone size={30} /></div>
          <span>This usually takes a few seconds.</span>
        </div>
      )}

      {entering && (
        <OtpInput
          value={code}
          disabled={stage === 'verifying'}
          onChange={(v) => { setCode(v); if (v.length === 6) void submit(v) }}
        />
      )}

      {stage === 'verifying' && (
        <div className="sending" role="status"><span className="spinner dark" /><span>Verifying…</span></div>
      )}

      <div className="form" style={{ marginTop: 14 }}>
        <ErrorAlert message={error} />
        <button
          className="btn btn-ghost resend"
          disabled={cooldown > 0 || waiting || stage === 'verifying'}
          onClick={() => void requestCode()}
        >
          {cooldown > 0 ? `Resend code in ${cooldown}s` : 'Send a new code'}
        </button>
      </div>
    </AuthShell>
  )
}
