import { AnimatePresence, motion } from 'motion/react'
import type { ReactNode } from 'react'

import { StepSidebar } from '@/components/layout/StepSidebar'
import { Progress } from '@/components/ui/progress'
import { STEP_LABELS, WIZARD_STEPS, useWizardStore, type WizardStep } from '@/stores/wizard-store'

interface AppShellProps {
  applicationId: string
  step: WizardStep
  completedSteps: Set<WizardStep>
  children: ReactNode
}

export function AppShell({ applicationId, step, completedSteps, children }: AppShellProps) {
  const direction = useWizardStore((s) => s.direction)
  const stepIndex = WIZARD_STEPS.indexOf(step)
  const progressPercent = Math.round(((stepIndex + 1) / WIZARD_STEPS.length) * 100)

  return (
    <div className="flex min-h-screen w-full bg-background">
      <StepSidebar applicationId={applicationId} completedSteps={completedSteps} />

      <div className="flex min-h-screen flex-1 flex-col">
        <div className="border-b border-border bg-card/60 px-6 py-4 backdrop-blur-sm lg:hidden">
          <div className="flex items-center justify-between text-sm">
            <span className="font-serif font-medium">GWEB</span>
            <span className="text-muted-foreground">{STEP_LABELS[step]}</span>
          </div>
          <Progress value={progressPercent} className="mt-3 h-1.5" />
        </div>

        <main className="mx-auto w-full max-w-2xl flex-1 px-6 py-12 sm:px-10">
          <AnimatePresence mode="wait" custom={direction}>
            <motion.div
              key={step}
              custom={direction}
              initial={{ opacity: 0, x: direction * 28 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: direction * -28 }}
              transition={{ duration: 0.32, ease: [0.22, 1, 0.36, 1] }}
            >
              {children}
            </motion.div>
          </AnimatePresence>
        </main>
      </div>
    </div>
  )
}
