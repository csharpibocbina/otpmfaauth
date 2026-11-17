export const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5258'

export class ApiError extends Error {
  status: number
  code?: string
  constructor(status: number, message: string, code?: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

async function request<T>(path: string, init: RequestInit & { token?: string | null } = {}): Promise<T> {
  const { token, ...rest } = init
  const res = await fetch(`${API_URL}${path}`, {
    ...rest,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...rest.headers,
    },
  })
  const text = await res.text()
  const body = text ? JSON.parse(text) : undefined
  if (!res.ok) throw new ApiError(res.status, body?.title ?? 'Request failed', body?.code)
  return body as T
}

export type LoginResponse =
  | { mfaRequired: false; accessToken: string }
  | { mfaRequired: true; mfaToken: string; methods: string[] }

export interface ChallengeResponse {
  challengeId: string
  maskedDestination: string
  expiresAt: string
}

export const api = {
  register: (email: string, password: string, phone?: string) =>
    request<{ id: string }>('/api/auth/register', { method: 'POST', body: JSON.stringify({ email, password, phone: phone || null }) }),
  login: (email: string, password: string) =>
    request<LoginResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  startChallenge: (mfaToken: string) =>
    request<ChallengeResponse>('/api/mfa/challenge', { method: 'POST', token: mfaToken }),
  verify: (mfaToken: string, challengeId: string, code: string) =>
    request<{ accessToken: string }>('/api/mfa/verify', { method: 'POST', token: mfaToken, body: JSON.stringify({ challengeId, code }) }),
  me: (token: string) => request<Profile>('/api/me', { token }),
}

export interface Profile {
  id: string
  email: string
  maskedPhone: string | null
  phoneVerified: boolean
  mfaEnabled: boolean
  memberSince: string
}
