import { AnimatePresence, motion } from 'motion/react'
import { useNavigate } from 'react-router'
import { useState } from 'react'
import { CheckCircle2, PartyPopper, ShieldCheck } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { StepFooter } from '@/components/shared/StepFooter'
import { StepHeader } from '@/components/shared/StepHeader'
import { useClassification, useDocuments, useEvaluation, useSubmitApplication } from '@/hooks/use-application'
import { ApiError } from '@/lib/api-client'
import { DOCUMENT_TYPE_LABELS, REQUIRED_DOCUMENT_TYPES, type ApplicationDetail, type SubmissionReadiness } from '@/lib/types'

export function ReviewStep({ application }: { application: ApplicationDetail }) {
  const navigate = useNavigate()
  const { data: documents } = useDocuments(application.id)
  const { data: classification } = useClassification(application.id)
  const { data: evaluation } = useEvaluation(application.id)
  const submit = useSubmitApplication(application.id)
  const [readiness, setReadiness] = useState<SubmissionReadiness | null>(null)

  const alreadySubmitted = application.status === 'Submitted'

  const handleSubmit = async () => {
    setReadiness(null)
    try {
      await submit.mutateAsync()
    } catch (error) {
      if (error instanceof ApiError && error.code === 'VALIDATION_FAILED') {
        setReadiness(error.details as SubmissionReadiness)
      }
    }
  }

  if (alreadySubmitted || submit.isSuccess) {
    return <SubmittedCelebration applicationId={application.id} />
  }

  const a = application.applicant
  const b = application.business

  return (
    <div>
      <StepHeader
        eyebrow="Step 6 of 6"
        title="Review & submit"
        description="A final look before this goes to manual underwriting review. Nothing here is auto-approved -- a human reviews every submission."
      />

      <div className="space-y-4">
        <ReviewCard title="Applicant">
          <Row label="Name" value={[a?.legalFirstName, a?.legalLastName].filter(Boolean).join(' ') || '--'} />
          <Row label="Email" value={a?.email ?? '--'} />
          <Row label="Role" value={a?.roleTitle ?? '--'} />
          <Row label="Government ID" value={a?.governmentId ? `${a.governmentId.type} ***${a.governmentId.last4}` : '--'} />
        </ReviewCard>

        <ReviewCard title="Business">
          <Row label="Legal name" value={b?.legalBusinessName ?? '--'} />
          <Row label="Entity type" value={b?.entityType ?? '--'} />
          <Row
            label="Registration"
            value={b?.registrationIdentifier ? `${b.registrationIdentifier.type} ${b.registrationIdentifier.maskedValue}` : '--'}
          />
          <Row
            label="Settlement account"
            value={b?.settlementBankAccount ? `${b.settlementBankAccount.bankName} ***${b.settlementBankAccount.last4}` : '--'}
          />
        </ReviewCard>

        <ReviewCard title="Documents">
          <div className="flex flex-wrap gap-2">
            {REQUIRED_DOCUMENT_TYPES.map((type) => {
              const doc = documents?.find((d) => d.type === type)
              return (
                <div key={type} className="flex items-center gap-1.5 rounded-full border border-border py-1 pr-3 pl-1">
                  {doc && (doc.status === 'Received' || doc.status === 'Accepted') ? (
                    <span className="flex size-5 items-center justify-center rounded-full bg-success/15 text-success">
                      <CheckCircle2 className="size-3.5" />
                    </span>
                  ) : (
                    <span className="size-5 rounded-full border-2 border-dashed border-muted-foreground/30" />
                  )}
                  <span className="text-xs font-medium">{DOCUMENT_TYPE_LABELS[type]}</span>
                </div>
              )
            })}
          </div>
        </ReviewCard>

        <ReviewCard title="Classification & evaluation">
          <Row
            label="MCC"
            value={
              classification
                ? `${classification.selfSelectedMccCode ?? classification.proposedMccCode ?? '--'}${classification.hasMismatch ? ' (flagged)' : ''}`
                : 'Not classified yet'
            }
          />
          <Row
            label="Effective rate"
            value={evaluation?.calculated ? `${evaluation.calculated.effectiveRatePercent.toFixed(2)}%` : '--'}
          />
          <Row label="Risk signals" value={evaluation ? `${evaluation.riskSignals.length} flagged` : '--'} />
        </ReviewCard>

        <AnimatePresence>
          {readiness && (
            <motion.div
              initial={{ opacity: 0, y: -8, height: 0 }}
              animate={{ opacity: 1, y: 0, height: 'auto' }}
              exit={{ opacity: 0, height: 0 }}
              className="rounded-xl border border-destructive/40 bg-destructive/5 p-4"
            >
              <p className="text-sm font-medium text-destructive">A few things are still missing</p>
              <ul className="mt-2 space-y-1 text-sm text-muted-foreground">
                {readiness.missingApplicantFields.map((f) => (
                  <li key={f}>&middot; Applicant: {f}</li>
                ))}
                {readiness.missingBusinessFields.map((f) => (
                  <li key={f}>&middot; Business: {f}</li>
                ))}
                {readiness.missingRequiredDocuments.map((d) => (
                  <li key={d}>&middot; Document: {DOCUMENT_TYPE_LABELS[d]}</li>
                ))}
              </ul>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      <StepFooter
        onBack={() => navigate(`/applications/${application.id}/evaluation`)}
        onContinue={handleSubmit}
        loading={submit.isPending}
        continueLabel="Submit for review"
      />
    </div>
  )
}

function ReviewCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card className="p-5">
      <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</p>
      <Separator className="my-3" />
      <div className="space-y-2">{children}</div>
    </Card>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium text-foreground">{value}</span>
    </div>
  )
}

function SubmittedCelebration({ applicationId }: { applicationId: string }) {
  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      className="flex flex-col items-center py-16 text-center"
    >
      <motion.div
        initial={{ scale: 0, rotate: -20 }}
        animate={{ scale: 1, rotate: 0 }}
        transition={{ type: 'spring', stiffness: 260, damping: 18, delay: 0.1 }}
        className="relative flex size-20 items-center justify-center rounded-full bg-success/15 text-success"
      >
        <ShieldCheck className="size-9" />
        {[0, 1, 2, 3, 4].map((i) => (
          <motion.span
            key={i}
            className="absolute text-accent"
            initial={{ opacity: 0, scale: 0, x: 0, y: 0 }}
            animate={{
              opacity: [1, 1, 0],
              scale: [0, 1, 1],
              x: Math.cos((i / 5) * Math.PI * 2) * 60,
              y: Math.sin((i / 5) * Math.PI * 2) * 60,
            }}
            transition={{ duration: 0.9, delay: 0.3 + i * 0.05, ease: 'easeOut' }}
          >
            <PartyPopper className="size-4" />
          </motion.span>
        ))}
      </motion.div>

      <motion.h1
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.35 }}
        className="mt-6 font-serif text-3xl font-medium text-foreground"
      >
        Submitted for review
      </motion.h1>
      <motion.p
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.45 }}
        className="mt-2 max-w-sm text-sm text-muted-foreground"
      >
        Your application is locked and waiting on manual underwriting review. No automated
        approval happens here -- a person reviews every submission.
      </motion.p>
      <motion.p
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 0.55 }}
        className="mt-4 flex items-center gap-2 rounded-full bg-secondary px-4 py-1.5 font-mono text-xs text-muted-foreground"
      >
        <Badge variant="success" className="text-[10px]">
          Ready for manual review
        </Badge>
        {applicationId}
      </motion.p>
    </motion.div>
  )
}
