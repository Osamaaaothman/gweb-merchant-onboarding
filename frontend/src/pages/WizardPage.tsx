import { Loader2 } from 'lucide-react'
import { Navigate, useParams } from 'react-router'

import { AppShell } from '@/components/layout/AppShell'
import { ApplicantStep } from '@/components/steps/ApplicantStep'
import { BusinessStep } from '@/components/steps/BusinessStep'
import { ClassificationStep } from '@/components/steps/ClassificationStep'
import { DocumentsStep } from '@/components/steps/DocumentsStep'
import { EvaluationStep } from '@/components/steps/EvaluationStep'
import { ReviewStep } from '@/components/steps/ReviewStep'
import {
  useApplication,
  useClassification,
  useDocuments,
  useEvaluation,
} from '@/hooks/use-application'
import { REQUIRED_DOCUMENT_TYPES } from '@/lib/types'
import { WIZARD_STEPS, type WizardStep } from '@/stores/wizard-store'

const SATISFIED = new Set(['Received', 'Accepted'])

export function WizardPage() {
  const { id, step } = useParams<{ id: string; step: string }>()
  const { data: application, isLoading, isError } = useApplication(id)
  const { data: documents } = useDocuments(id)
  const { data: classification } = useClassification(id)
  const { data: evaluation } = useEvaluation(id)

  if (!id) {
    return <Navigate to="/" replace />
  }

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    )
  }

  if (isError || !application) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-3 bg-background text-center">
        <p className="font-serif text-2xl text-foreground">Application not found</p>
        <p className="text-sm text-muted-foreground">
          Double-check the application ID, or start a new application.
        </p>
      </div>
    )
  }

  if (!step || !WIZARD_STEPS.includes(step as WizardStep)) {
    return <Navigate to={`/applications/${id}/applicant`} replace />
  }

  const completedSteps = new Set<WizardStep>()
  if (application.completeness.missingApplicantFields.length === 0 && application.applicant) {
    completedSteps.add('applicant')
  }
  if (application.completeness.missingBusinessFields.length === 0 && application.business) {
    completedSteps.add('business')
  }
  if (
    documents &&
    REQUIRED_DOCUMENT_TYPES.every((type) =>
      documents.some((d) => d.type === type && SATISFIED.has(d.status)),
    )
  ) {
    completedSteps.add('documents')
  }
  if (classification?.selfSelectedMccCode) {
    completedSteps.add('classification')
  }
  if (evaluation?.status === 'Completed') {
    completedSteps.add('evaluation')
  }
  if (application.status === 'Submitted') {
    completedSteps.add('review')
  }

  return (
    <AppShell applicationId={id} step={step as WizardStep} completedSteps={completedSteps}>
      {step === 'applicant' && <ApplicantStep application={application} />}
      {step === 'business' && <BusinessStep application={application} />}
      {step === 'documents' && <DocumentsStep application={application} />}
      {step === 'classification' && <ClassificationStep application={application} />}
      {step === 'evaluation' && <EvaluationStep application={application} />}
      {step === 'review' && <ReviewStep application={application} />}
    </AppShell>
  )
}
