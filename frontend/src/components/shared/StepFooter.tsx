import { ArrowLeft, ArrowRight, Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'

interface StepFooterProps {
  onBack?: () => void
  onContinue?: () => void
  continueLabel?: string
  continueDisabled?: boolean
  loading?: boolean
  continueType?: 'button' | 'submit'
}

export function StepFooter({
  onBack,
  onContinue,
  continueLabel = 'Continue',
  continueDisabled,
  loading,
  continueType = 'button',
}: StepFooterProps) {
  return (
    <div className="mt-10 flex items-center justify-between border-t border-border pt-6">
      {onBack ? (
        <Button type="button" variant="ghost" onClick={onBack}>
          <ArrowLeft className="size-4" />
          Back
        </Button>
      ) : (
        <span />
      )}
      <Button
        type={continueType}
        onClick={continueType === 'button' ? onContinue : undefined}
        disabled={continueDisabled || loading}
        size="lg"
      >
        {loading ? <Loader2 className="size-4 animate-spin" /> : null}
        {continueLabel}
        {!loading && <ArrowRight className="size-4" />}
      </Button>
    </div>
  )
}
