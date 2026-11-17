import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useEffect } from 'react'
import { API_URL } from '../api/client'

export interface NotificationHandlers {
  OtpSent?: (p: { challengeId: string }) => void
  OtpDeliveryFailed?: (p: { challengeId: string; reason: string }) => void
  MfaVerified?: (p: { challengeId: string }) => void
  MfaFailed?: (p: { challengeId: string; reason: string }) => void
}

/** Opens the SignalR connection with the current token and wires server events to handlers. */
export function useNotifications(token: string | null, handlers: NotificationHandlers) {
  useEffect(() => {
    if (!token) return
    const connection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/notifications`, { accessTokenFactory: () => token })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    // SignalR sends camelCase JSON by default.
    connection.on('otpSent', (p) => handlers.OtpSent?.(p))
    connection.on('otpDeliveryFailed', (p) => handlers.OtpDeliveryFailed?.(p))
    connection.on('mfaVerified', (p) => handlers.MfaVerified?.(p))
    connection.on('mfaFailed', (p) => handlers.MfaFailed?.(p))

    connection.start().catch((e) => console.warn('SignalR connect failed', e))
    return () => {
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
    // handlers intentionally excluded: callers pass fresh closures each render
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token])
}
