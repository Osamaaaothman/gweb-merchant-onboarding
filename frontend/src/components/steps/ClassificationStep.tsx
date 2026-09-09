import { motion } from 'motion/react'
import { useDeferredValue, useState } from 'react'
import { useNavigate } from 'react-router'
import { AlertTriangle, Loader2, Search, Sparkles } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { StepFooter } from '@/components/shared/StepFooter'
import { StepHeader } from '@/components/shared/StepHeader'
import { useClassification, useClassify, useConfirmClassification, useMccSearch } from '@/hooks/use-application'
import { cn } from '@/lib/utils'
import type { ApplicationDetail } from '@/lib/types'

export function ClassificationStep({ application }: { application: ApplicationDetail }) {
  const navigate = useNavigate()
  const { data: classification, isLoading } = useClassification(application.id)
  const classify = useClassify(application.id)
  const confirm = useConfirmClassification(application.id)
  const [selected, setSelected] = useState<string | null>(null)
  const [manualQuery, setManualQuery] = useState('')
  const deferredQuery = useDeferredValue(manualQuery)
  const { data: searchResults, isFetching: isSearching } = useMccSearch(deferredQuery)

  const activeCode = selected ?? classification?.selfSelectedMccCode ?? classification?.proposedMccCode ?? null
  const activeCodeNotInCandidates =
    activeCode && !classification?.candidates.some((c) => c.mccCode === activeCode)
  const activeCodeDetail = activeCodeNotInCandidates
    ? searchResults?.results.find((r) => r.code === activeCode)
    : undefined

  return (
    <div>
      <StepHeader
        eyebrow="Step 4 of 6"
        title="Business classification"
        description="Based on the business description you provided, we'll propose a Merchant Category Code (MCC). Confirm it, or search for a different one yourself."
      />

      {!classification && !isLoading && (
        <Card className="items-center gap-4 border-dashed py-14 text-center">
          <div className="mx-auto flex size-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Sparkles className="size-5" />
          </div>
          <div>
            <p className="font-medium text-foreground">Ready to classify</p>
            <p className="mx-auto mt-1 max-w-sm text-sm text-muted-foreground">
              We'll analyze "{application.business?.businessDescription?.slice(0, 80) ?? 'your business description'}"
              against the MCC catalog.
            </p>
          </div>
          <Button onClick={() => classify.mutate()} disabled={classify.isPending} className="mx-auto">
            {classify.isPending ? 'Classifying...' : 'Get MCC suggestion'}
          </Button>
        </Card>
      )}

      {classification && (
        <div className="space-y-3">
          {classification.hasMismatch && (
            <div className="flex items-center gap-2 rounded-lg border border-warning/40 bg-warning/10 px-3.5 py-2.5 text-sm text-warning-foreground">
              <AlertTriangle className="size-4 shrink-0" />
              Your selection differs from the proposed code -- flagged for manual review.
            </div>
          )}

          {classification.candidates.map((candidate, index) => {
            const isActive = candidate.mccCode === activeCode
            const isProposed = candidate.mccCode === classification.proposedMccCode
            return (
              <motion.button
                key={candidate.mccCode}
                type="button"
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: index * 0.05 }}
                onClick={() => setSelected(candidate.mccCode)}
                className={cn(
                  'w-full rounded-xl border p-4 text-left transition-all',
                  isActive
                    ? 'border-primary bg-primary/5 ring-1 ring-primary/20'
                    : 'border-border bg-card hover:border-primary/30',
                )}
              >
                <div className="flex items-center justify-between gap-3">
                  <div className="flex items-center gap-2.5">
                    <span className="font-mono text-sm font-semibold text-foreground">{candidate.mccCode}</span>
                    {isProposed && <Badge variant="premium">Proposed</Badge>}
                  </div>
                  <span className="text-xs font-medium text-muted-foreground">
                    {Math.round(candidate.confidence * 100)}% confidence
                  </span>
                </div>
                <div className="mt-2 h-1.5 w-full overflow-hidden rounded-full bg-secondary">
                  <motion.div
                    className="h-full rounded-full bg-primary"
                    initial={{ width: 0 }}
                    animate={{ width: `${candidate.confidence * 100}%` }}
                    transition={{ duration: 0.6, delay: 0.15 + index * 0.05, ease: 'easeOut' }}
                  />
                </div>
                <p className="mt-2.5 text-sm text-muted-foreground">{candidate.explanation}</p>
              </motion.button>
            )
          })}

          <Button variant="ghost" size="sm" onClick={() => classify.mutate()} disabled={classify.isPending}>
            {classify.isPending ? 'Re-classifying...' : 'Re-run classification'}
          </Button>
        </div>
      )}

      <div className="mt-8">
        <p className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
          Not right? Search for a code yourself
        </p>
        <div className="relative">
          <Search className="absolute top-1/2 left-3.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={manualQuery}
            onChange={(e) => setManualQuery(e.target.value)}
            placeholder="Search by code or keyword -- e.g. &quot;sports club&quot; or 7997"
            className="pl-10"
          />
        </div>

        {activeCodeNotInCandidates && (
          <div className="mt-2 flex items-center justify-between rounded-lg border border-primary/30 bg-primary/5 px-3.5 py-2.5 text-sm">
            <span>
              <span className="font-mono font-semibold text-foreground">{activeCode}</span>
              {activeCodeDetail && <span className="ml-2 text-muted-foreground">{activeCodeDetail.description}</span>}
              {!activeCodeDetail && <span className="ml-2 text-muted-foreground">Selected manually</span>}
            </span>
            <Badge variant="premium">Selected</Badge>
          </div>
        )}

        {manualQuery.length > 0 && (
          <div className="mt-2 max-h-64 space-y-1 overflow-y-auto rounded-lg border border-border bg-card p-1.5">
            {isSearching && (
              <p className="flex items-center gap-2 px-2.5 py-2 text-xs text-muted-foreground">
                <Loader2 className="size-3.5 animate-spin" /> Searching...
              </p>
            )}
            {!isSearching && searchResults?.results.length === 0 && (
              <p className="px-2.5 py-2 text-xs text-muted-foreground">
                No catalog matches for "{manualQuery}" -- try a shorter or different keyword.
              </p>
            )}
            {searchResults?.results.map((code) => (
              <button
                key={code.code}
                type="button"
                onClick={() => setSelected(code.code)}
                className={cn(
                  'flex w-full items-center justify-between gap-3 rounded-md px-2.5 py-2 text-left text-sm transition-colors hover:bg-secondary',
                  activeCode === code.code && 'bg-primary/5 ring-1 ring-primary/20',
                )}
              >
                <span className="min-w-0">
                  <span className="font-mono font-medium text-foreground">{code.code}</span>
                  <span className="ml-2 text-muted-foreground">{code.description}</span>
                </span>
                <Badge variant="outline" className="shrink-0 text-[10px]">
                  {code.category}
                </Badge>
              </button>
            ))}
          </div>
        )}
      </div>

      <StepFooter
        onBack={() => navigate(`/applications/${application.id}/documents`)}
        onContinue={async () => {
          if (activeCode && activeCode !== classification?.selfSelectedMccCode) {
            await confirm.mutateAsync(activeCode)
          }
          navigate(`/applications/${application.id}/evaluation`)
        }}
        continueDisabled={!activeCode}
        loading={confirm.isPending}
        continueLabel={activeCode ? `Confirm MCC ${activeCode} and continue` : 'Select a code to continue'}
      />
    </div>
  )
}
