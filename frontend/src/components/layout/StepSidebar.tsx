import { motion } from 'motion/react'
import { Check } from 'lucide-react'
import { Link, useParams } from 'react-router'

import { cn } from '@/lib/utils'
import { STEP_LABELS, WIZARD_STEPS, useWizardStore, type WizardStep } from '@/stores/wizard-store'

interface StepSidebarProps {
  applicationId: string
  completedSteps: Set<WizardStep>
}

export function StepSidebar({ applicationId, completedSteps }: StepSidebarProps) {
  const { step: currentStep } = useParams<{ step: WizardStep }>()
  const visitedSteps = useWizardStore((s) => s.visitedSteps)
  const setDirection = useWizardStore((s) => s.setDirection)

  return (
    <aside className="hidden w-72 shrink-0 bg-sidebar text-sidebar-foreground lg:flex lg:flex-col">
      <div className="px-8 pt-10 pb-8">
        <p className="font-serif text-[1.35rem] font-medium tracking-tight text-sidebar-foreground">
          GWEB
        </p>
        <p className="mt-0.5 text-xs tracking-wide text-sidebar-foreground/50 uppercase">
          Merchant Onboarding
        </p>
      </div>

      <nav className="flex-1 px-6">
        <ol className="relative flex flex-col">
          {WIZARD_STEPS.map((step, index) => {
            const isCurrent = step === currentStep
            const isDone = completedSteps.has(step)
            const isReachable = visitedSteps.has(step) || isDone
            const stepIndex = WIZARD_STEPS.indexOf(currentStep as WizardStep)

            return (
              <li key={step} className="relative flex gap-4 pb-8 last:pb-0">
                {index < WIZARD_STEPS.length - 1 && (
                  <span className="absolute top-7 left-[13px] h-full w-px bg-sidebar-border">
                    <motion.span
                      className="absolute inset-x-0 top-0 w-px bg-sidebar-primary"
                      initial={false}
                      animate={{ height: isDone ? '100%' : '0%' }}
                      transition={{ duration: 0.45, ease: 'easeInOut' }}
                    />
                  </span>
                )}

                <Link
                  to={isReachable ? `/applications/${applicationId}/${step}` : '#'}
                  onClick={() => setDirection(index > stepIndex ? 1 : -1)}
                  aria-disabled={!isReachable}
                  className={cn(
                    'relative z-10 mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full border text-xs font-medium transition-colors',
                    isDone
                      ? 'border-sidebar-primary bg-sidebar-primary text-sidebar-primary-foreground'
                      : isCurrent
                        ? 'border-sidebar-primary text-sidebar-primary'
                        : 'border-sidebar-border text-sidebar-foreground/40',
                    !isReachable && 'pointer-events-none',
                  )}
                >
                  {isDone ? (
                    <motion.span
                      initial={{ scale: 0 }}
                      animate={{ scale: 1 }}
                      transition={{ type: 'spring', stiffness: 500, damping: 22 }}
                    >
                      <Check className="size-3.5" />
                    </motion.span>
                  ) : (
                    index + 1
                  )}
                </Link>

                <Link
                  to={isReachable ? `/applications/${applicationId}/${step}` : '#'}
                  onClick={() => setDirection(index > stepIndex ? 1 : -1)}
                  aria-disabled={!isReachable}
                  className={cn(
                    'pt-1 text-sm font-medium transition-colors',
                    isCurrent
                      ? 'text-sidebar-foreground'
                      : isReachable
                        ? 'text-sidebar-foreground/70 hover:text-sidebar-foreground'
                        : 'pointer-events-none text-sidebar-foreground/30',
                  )}
                >
                  {STEP_LABELS[step]}
                </Link>
              </li>
            )
          })}
        </ol>
      </nav>

      <div className="border-t border-sidebar-border px-8 py-6">
        <p className="text-[11px] text-sidebar-foreground/40">Application</p>
        <p className="mt-0.5 truncate font-mono text-[11px] text-sidebar-foreground/60">
          {applicationId}
        </p>
      </div>
    </aside>
  )
}
