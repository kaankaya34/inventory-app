export interface Toast {
  id: number
  kind: 'success' | 'error' | 'info'
  text: string
}

const tones: Record<Toast['kind'], string> = {
  success: 'border-emerald-500/50 bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200',
  error: 'border-rose-500/50 bg-rose-50 text-rose-800 dark:bg-rose-950 dark:text-rose-200',
  info: 'border-sky-500/50 bg-sky-50 text-sky-800 dark:bg-sky-950 dark:text-sky-200',
}

export function Toasts({ toasts }: { toasts: Toast[] }) {
  return (
    <div className="pointer-events-none fixed bottom-4 right-4 z-50 flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2">
      {toasts.map((t) => (
        <div key={t.id} className={`slide-in rounded-lg border px-3 py-2 text-sm shadow-lg ${tones[t.kind]}`}>
          {t.text}
        </div>
      ))}
    </div>
  )
}
