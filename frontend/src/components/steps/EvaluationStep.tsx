import { motion } from 'motion/react'
import { useNavigate } from 'react-router'
import { AlertCircle, Clock3, Sparkles } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { StepFooter } from '@/components/shared/StepFooter'
import { StepHeader } from '@/components/shared/StepHeader'
import { useDocuments, useEvaluate, useEvaluation } from '@/hooks/use-application'
import type { ApplicationDetail } from '@/lib/types'

function currency(value: number) {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value)
}

export function EvaluationStep({ application }: { application: ApplicationDetail }) {
  const navigate = useNavigate()
  const { data: documents } = useDocuments(application.id)
  const { data: evaluation, isLoading } = useEvaluation(application.id)
  const evaluate = useEvaluate(application.id)

  const statementDoc = documents?.find((d) => d.type === 'ProcessingStatement')

  const runEvaluation = () => evaluate.mutate(statementDoc?.id ?? null)

  return (
    <div>
      <StepHeader
        eyebrow="Step 5 of 6"
        title="Rate & business evaluation"
        description="If a current processing statement was uploaded, we'll extract and compare its rates. Either way, we'll flag anything that may need manual review."
      />

      {!evaluation && !isLoading && (
        <Card className="items-center gap-4 border-dashed py-14 text-center">
          <div className="mx-auto flex size-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Sparkles className="size-5" />
          </div>
          <div>
            <p className="font-medium text-foreground">
              {statementDoc ? 'Ready to evaluate with your processing statement' : 'Ready to run risk evaluation'}
            </p>
            <p className="mx-auto mt-1 max-w-sm text-sm text-muted-foreground">
              {statementDoc
                ? `Using ${statementDoc.originalFilename} to extract current rates.`
                : 'No processing statement was uploaded -- we can still check the business profile for risk signals.'}
            </p>
          </div>
          <Button onClick={runEvaluation} disabled={evaluate.isPending} className="mx-auto">
            {evaluate.isPending ? 'Evaluating...' : 'Run evaluation'}
          </Button>
        </Card>
      )}

      {evaluation?.status === 'Processing' && (
        <Card className="items-center gap-3 border-warning/40 bg-warning/5 py-10 text-center">
          <Clock3 className="mx-auto size-6 text-warning" />
          <p className="font-medium text-foreground">Still processing</p>
          <p className="text-sm text-muted-foreground">
            This is taking a little longer than usual. It'll be ready shortly -- you can continue and check
            back, or refresh now.
          </p>
          <Button variant="outline" size="sm" onClick={runEvaluation}>
            Check again
          </Button>
        </Card>
      )}

      {evaluation?.status === 'Completed' && (
        <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-5">
          {evaluation.calculated && (
            <Card className="p-6">
              <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                Effective rate (calculated)
              </p>
              <p className="mt-1 font-serif text-3xl font-medium text-foreground">
                {evaluation.calculated.effectiveRatePercent.toFixed(2)}%
              </p>
              <Separator className="my-4" />
              <div className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
                <Stat label="Total monthly cost" value={currency(evaluation.calculated.totalMonthlyCostAmount)} />
                <Stat label="Discount fee" value={currency(evaluation.calculated.discountFeeAmount)} />
                <Stat label="Transaction fee" value={currency(evaluation.calculated.transactionFeeAmount)} />
                <Stat label="Monthly fee" value={currency(evaluation.calculated.monthlyFeeAmount)} />
              </div>
            </Card>
          )}

          {evaluation.extracted && (
            <Card className="p-6">
              <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                Extracted from statement
              </p>
              <dl className="mt-3 grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
                <DL label="Processor" value={evaluation.extracted.processor} />
                <DL
                  label="Monthly volume"
                  value={evaluation.extracted.monthlyVolume ? currency(evaluation.extracted.monthlyVolume) : null}
                />
                <DL
                  label="Discount rate"
                  value={
                    evaluation.extracted.discountRatePercent
                      ? `${evaluation.extracted.discountRatePercent}%`
                      : null
                  }
                />
                <DL label="Statement period" value={evaluation.extracted.statementPeriod} />
              </dl>
              {evaluation.commentary && (
                <p className="mt-4 border-t border-border pt-4 text-sm text-muted-foreground italic">
                  "{evaluation.commentary}"
                </p>
              )}
            </Card>
          )}

          <div>
            <p className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
              Risk signals ({evaluation.riskSignals.length})
            </p>
            {evaluation.riskSignals.length === 0 ? (
              <p className="text-sm text-muted-foreground">No risk signals detected.</p>
            ) : (
              <div className="space-y-2">
                {evaluation.riskSignals.map((signal) => (
                  <div
                    key={signal.code}
                    className="flex items-start gap-2.5 rounded-lg border border-warning/30 bg-warning/5 px-3.5 py-2.5"
                  >
                    <AlertCircle className="mt-0.5 size-4 shrink-0 text-warning" />
                    <div>
                      <p className="text-sm text-foreground">{signal.message}</p>
                      <Badge variant="outline" className="mt-1.5 text-[10px]">
                        {signal.sourceField}
                      </Badge>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </motion.div>
      )}

      <StepFooter
        onBack={() => navigate(`/applications/${application.id}/classification`)}
        onContinue={() => navigate(`/applications/${application.id}/review`)}
        continueDisabled={!evaluation}
        continueLabel={evaluation ? 'Continue to review' : 'Run evaluation to continue'}
      />
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="mt-0.5 font-medium text-foreground">{value}</p>
    </div>
  )
}

function DL({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 font-medium text-foreground">{value ?? '--'}</dd>
    </div>
  )
}
