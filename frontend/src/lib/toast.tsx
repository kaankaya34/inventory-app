import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react'
import { Toasts, type Toast } from '../components/Toasts'

type ToastFn = (kind: Toast['kind'], text: string) => void

const ToastContext = createContext<ToastFn>(() => undefined)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const nextId = useRef(0)

  const toast = useCallback<ToastFn>((kind, text) => {
    const id = ++nextId.current
    setToasts((t) => [...t.slice(-3), { id, kind, text }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4000)
  }, [])

  return (
    <ToastContext.Provider value={toast}>
      {children}
      <Toasts toasts={toasts} />
    </ToastContext.Provider>
  )
}

export const useToast = () => useContext(ToastContext)
