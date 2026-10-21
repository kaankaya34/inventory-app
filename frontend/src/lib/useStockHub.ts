import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { API_BASE, getAuthToken } from './api'
import type { Product, Reservation, StockLog } from './types'

export type HubStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

export interface HubHandlers {
  onProduct?: (p: Product) => void
  onReservation?: (r: Reservation) => void
  onLog?: (l: StockLog) => void
  onConnected?: () => void
}

// Admin JWT'si varsa bağlantı ADMIN grubuna alınır ve rezervasyon/log olayları da gelir.
export function useStockHub(handlers: HubHandlers): HubStatus {
  const [status, setStatus] = useState<HubStatus>('connecting')
  const ref = useRef(handlers)

  useEffect(() => {
    ref.current = handlers
  })

  useEffect(() => {
    let disposed = false
    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/stock`, { accessTokenFactory: () => getAuthToken() ?? '' })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('ProductUpdated', (p: Product) => ref.current.onProduct?.(p))
    connection.on('ReservationUpdated', (r: Reservation) => ref.current.onReservation?.(r))
    connection.on('StockLogCreated', (l: StockLog) => ref.current.onLog?.(l))
    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => {
      setStatus('connected')
      ref.current.onConnected?.()
    })
    connection.onclose(() => {
      if (!disposed) setStatus('disconnected')
    })

    const start = async () => {
      while (!disposed) {
        try {
          await connection.start()
          if (disposed) return
          setStatus('connected')
          ref.current.onConnected?.()
          return
        } catch {
          if (disposed) return
          setStatus('disconnected')
          await new Promise((r) => setTimeout(r, 3000))
        }
      }
    }
    void start()

    return () => {
      disposed = true
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [])

  return status
}
