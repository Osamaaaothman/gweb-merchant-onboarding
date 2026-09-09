// Mirrors src/Gweb.Api's response/request DTOs field-for-field. Kept as one file since
// the backend's API surface is small and stable within one phase; if this grows past a
// handful of features, generating these from the backend (NSwag/OpenAPI) instead of
// hand-maintaining would be the next move -- not needed yet.

export type ApplicationStatus = 'InProgress' | 'Submitted'

export type GovernmentIdType = 'DriversLicense' | 'Passport' | 'NationalId'

export type EntityType =
  | 'Llc'
  | 'Corporation'
  | 'Partnership'
  | 'SoleProprietor'
  | 'Nonprofit'
  | 'Other'

export type RegistrationIdentifierType = 'Ein' | 'StateUbi' | 'Other'

export type DocumentType =
  | 'GovernmentId'
  | 'BusinessRegistration'
  | 'BusinessLicense'
  | 'BankEvidence'
  | 'ProcessingStatement'
  | 'AdditionalEvidence'

export type DocumentStatus =
  | 'Requested'
  | 'Uploading'
  | 'Received'
  | 'Processing'
  | 'Accepted'
  | 'NeedsReview'
  | 'Rejected'

export type EvaluationStatus = 'Processing' | 'Completed'

export interface Address {
  line1: string
  line2?: string | null
  city: string
  state: string
  postalCode: string
  country: string
}

export interface GovernmentIdView {
  type: GovernmentIdType
  last4: string
}

export interface ApplicantView {
  legalFirstName?: string | null
  legalMiddleName?: string | null
  legalLastName?: string | null
  dateOfBirth?: string | null
  residentialAddress?: Address | null
  email?: string | null
  phone?: string | null
  roleTitle?: string | null
  ownershipPercentage?: number | null
  governmentId?: GovernmentIdView | null
  consentVersion?: string | null
  version: number
}

export interface RegistrationIdentifierView {
  type: RegistrationIdentifierType
  maskedValue: string
}

export interface VolumeProfile {
  expectedAnnualCardVolume: number
  averageTicket: number
  highestTicket: number
  monthlyTransactionCount: number
  cardPresentPercentage: number
  ecommercePercentage: number
}

export interface BeneficialOwner {
  name: string
  roleTitle: string
  ownershipPercentage: number
}

export interface SettlementBankAccountView {
  accountHolder: string
  bankName: string
  last4: string
  statementDate: string
}

export interface BusinessView {
  legalBusinessName?: string | null
  dbaName?: string | null
  entityType?: EntityType | null
  formationCountry?: string | null
  formationState?: string | null
  registrationIdentifier?: RegistrationIdentifierView | null
  registeredAddress?: Address | null
  operatingAddress?: Address | null
  websiteUrl?: string | null
  businessDescription?: string | null
  businessStartDate?: string | null
  volumeProfile?: VolumeProfile | null
  beneficialOwners: BeneficialOwner[]
  settlementBankAccount?: SettlementBankAccountView | null
  existingProcessor?: string | null
  version: number
}

export interface Completeness {
  isComplete: boolean
  missingApplicantFields: string[]
  missingBusinessFields: string[]
}

export interface ApplicationDetail {
  id: string
  status: ApplicationStatus
  version: number
  createdAt: string
  updatedAt: string
  applicant: ApplicantView | null
  business: BusinessView | null
  completeness: Completeness
  correlationId: string
}

export interface ApplicationSummary {
  id: string
  status: ApplicationStatus
  version: number
  createdAt: string
  updatedAt: string
  correlationId: string
}

export interface DocumentView {
  id: string
  applicationId: string
  type: DocumentType
  status: DocumentStatus
  originalFilename: string
  contentType: string
  declaredSizeBytes: number
  actualSizeBytes?: number | null
  uploadedAt?: string | null
  rejectionReason?: string | null
  version: number
}

export interface PresignedUpload {
  document: DocumentView
  uploadUrl: string
  uploadFields: Record<string, string>
}

export interface McCode {
  code: string
  description: string
  category: string
}

export interface McSearchResult {
  query: string | null
  results: McCode[]
}

export interface McClassificationCandidate {
  mccCode: string
  confidence: number
  explanation: string
}

export interface McClassification {
  candidates: McClassificationCandidate[]
  proposedMccCode?: string | null
  proposedProvider?: string | null
  classifiedAt?: string | null
  selfSelectedMccCode?: string | null
  selfSelectedAt?: string | null
  hasMismatch: boolean
  version: number
}

export interface StatementExtraction {
  processor?: string | null
  monthlyVolume?: number | null
  discountRatePercent?: number | null
  perTransactionFee?: number | null
  monthlyFee?: number | null
  chargebackFeeTotal?: number | null
  statementPeriod?: string | null
  provider: string
}

export interface EffectiveRate {
  totalMonthlyCostAmount: number
  effectiveRatePercent: number
  discountFeeAmount: number
  transactionFeeAmount: number
  monthlyFeeAmount: number
  chargebackFeeAmount: number
}

export interface RiskSignal {
  code: string
  message: string
  sourceField: string
  sourceDocumentId?: string | null
}

export interface Evaluation {
  status: EvaluationStatus
  extracted?: StatementExtraction | null
  calculated?: EffectiveRate | null
  commentary?: string | null
  riskSignals: RiskSignal[]
  processingStatementDocumentId?: string | null
  evaluatedAt?: string | null
  version: number
}

export interface SubmissionReadiness {
  missingApplicantFields: string[]
  missingBusinessFields: string[]
  missingRequiredDocuments: DocumentType[]
}

export interface SubmitResult {
  applicationId: string
  status: ApplicationStatus
  submittedAt: string
  version: number
  applicant: ApplicantView | null
  business: BusinessView | null
  documents: DocumentView[]
  classification: McClassification | null
  evaluation: Evaluation | null
}

export interface ApiErrorBody {
  error: {
    code: string
    message: string
    details: unknown
  }
  correlationId: string | null
}

export const REQUIRED_DOCUMENT_TYPES: DocumentType[] = [
  'GovernmentId',
  'BusinessRegistration',
  'BankEvidence',
]

export const DOCUMENT_TYPE_LABELS: Record<DocumentType, string> = {
  GovernmentId: 'Government ID',
  BusinessRegistration: 'Business Registration',
  BusinessLicense: 'Business License',
  BankEvidence: 'Bank Evidence',
  ProcessingStatement: 'Processing Statement',
  AdditionalEvidence: 'Additional Evidence',
}
