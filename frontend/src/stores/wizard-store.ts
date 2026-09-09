import { create } from 'zustand'

export const WIZARD_STEPS = [
  'applicant',
  'business',
  'documents',
  'classification',
  'evaluation',
  'review',
] as const

export type WizardStep = (typeof WIZARD_STEPS)[number]

export const STEP_LABELS: Record<WizardStep, string> = {
  applicant: 'Applicant',
  business: 'Business',
  documents: 'Documents',
  classification: 'Classification',
  evaluation: 'Rate Evaluation',
  review: 'Review & Submit',
}

interface WizardState {
  /** +1 forward / -1 backward -- drives which direction the step transition animates. */
  direction: 1 | -1
  visitedSteps: Set<WizardStep>
  setDirection: (direction: 1 | -1) => void
  markVisited: (step: WizardStep) => void
}

export const useWizardStore = create<WizardState>((set) => ({
  direction: 1,
  visitedSteps: new Set(['applicant']),
  setDirection: (direction) => set({ direction }),
  markVisited: (step) =>
    set((state) => ({ visitedSteps: new Set(state.visitedSteps).add(step) })),
}))
