import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AuthProvider } from '../auth/AuthContext'
import { MfaChallengePage } from './MfaChallengePage'
import type { NotificationHandlers } from '../realtime/useNotifications'

let handlers: NotificationHandlers = {}

vi.mock('../realtime/useNotifications', () => ({
  useNotifications: (_token: string | null, h: NotificationHandlers) => { handlers = h },
}))

vi.mock('../api/client', async (orig) => ({
  ...(await orig<typeof import('../api/client')>()),
  api: {
    startChallenge: vi.fn().mockResolvedValue({ challengeId: 'c1', maskedDestination: '+6•••••9811', expiresAt: '' }),
    verify: vi.fn().mockResolvedValue({ accessToken: 'final-access-token' }),
  },
}))

function renderPage() {
  return render(
    <AuthProvider initialMfaToken="mfa-token">
      <MemoryRouter initialEntries={['/mfa']}>
        <Routes>
          <Route path="/" element={<h1>Welcome page</h1>} />
          <Route path="/login" element={<h1>Login page</h1>} />
          <Route path="/mfa" element={<MfaChallengePage />} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  )
}

describe('MfaChallengePage', () => {
  it('lands on the welcome page (not login) after a correct code', async () => {
    renderPage()
    await screen.findByText(/Sending your verification code/i)
    act(() => handlers.OtpSent?.({ challengeId: 'c1' }))

    screen.getByLabelText('Digit 1').focus()
    await userEvent.paste('123456')

    expect(await screen.findByText('Welcome page')).toBeTruthy()
    await waitFor(() => expect(screen.queryByText('Login page')).toBeNull())
  })
})
