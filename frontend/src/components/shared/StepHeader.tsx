import { motion } from 'motion/react'
import type { ReactNode } from 'react'

interface StepHeaderProps {
  eyebrow: string
  title: string
  description?: string
  action?: ReactNode
}

export function StepHeader({ eyebrow, title, description, action }: StepHeaderProps) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35, delay: 0.05 }}
      className="mb-9 flex items-start justify-between gap-4"
    >
      <div>
        <p className="text-xs font-medium tracking-widest text-primary uppercase">{eyebrow}</p>
        <h1 className="mt-2 font-serif text-3xl font-medium tracking-tight text-foreground">
          {title}
        </h1>
        {description && (
          <p className="mt-2 max-w-lg text-sm leading-relaxed text-muted-foreground">
            {description}
          </p>
        )}
      </div>
      {action}
    </motion.div>
  )
}
