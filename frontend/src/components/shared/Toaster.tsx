import { AnimatePresence, motion } from 'motion/react'
import { CheckCircle2, Info, X, XCircle } from 'lucide-react'

import { useToastStore } from '@/stores/toast-store'
import { cn } from '@/lib/utils'

const ICONS = {
  success: CheckCircle2,
  error: XCircle,
  info: Info,
}

export function Toaster() {
  const toasts = useToastStore((s) => s.toasts)
  const dismiss = useToastStore((s) => s.dismiss)

  return (
    <div className="pointer-events-none fixed inset-x-0 top-4 z-[100] flex flex-col items-center gap-2 px-4 sm:right-4 sm:left-auto sm:items-end">
      <AnimatePresence mode="popLayout">
        {toasts.map((t) => {
          const Icon = ICONS[t.variant]
          return (
            <motion.div
              key={t.id}
              layout
              initial={{ opacity: 0, y: -24, scale: 0.9 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, scale: 0.9, transition: { duration: 0.15 } }}
              transition={{ type: 'spring', stiffness: 400, damping: 28 }}
              className="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-xl border border-border bg-card p-4 shadow-lg"
            >
              <Icon
                className={cn(
                  'mt-0.5 size-5 shrink-0',
                  t.variant === 'success' && 'text-success',
                  t.variant === 'error' && 'text-destructive',
                  t.variant === 'info' && 'text-primary',
                )}
              />
              <div className="flex-1 space-y-0.5">
                <p className="text-sm font-medium text-foreground">{t.title}</p>
                {t.description && (
                  <p className="text-xs text-muted-foreground">{t.description}</p>
                )}
              </div>
              <button
                onClick={() => dismiss(t.id)}
                className="text-muted-foreground/60 transition-colors hover:text-foreground"
              >
                <X className="size-4" />
              </button>
            </motion.div>
          )
        })}
      </AnimatePresence>
    </div>
  )
}
