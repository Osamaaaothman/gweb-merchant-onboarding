import { z } from 'zod'

// Mirrors src/Gweb.Domain/Applications/Applicant.cs and Business.cs's ApplyUpdate
// validators field-for-field, so the client rejects the same input the server would
// -- per brief "Client validation mirroring server rules." The server remains the
// source of truth (these rules are duplicated, not shared code, since the two run in
// different languages); this schema is intentionally a close mirror, not a
// reinterpretation.
//
// Numeric HTML inputs are kept as plain strings in the schema (not z.coerce.number())
// -- react-hook-form's register() always reads <input> values as strings, and mixing
// z.coerce fields with plain-string fields under zodResolver's generic inference
// produced an unrelated-input/output-type error with this zod4 + @hookform/resolvers5
// combination. Numbers are parsed at the point of building each PATCH body instead.

const numberString = (message: string) =>
  z.string().refine((v) => v.trim() === '' || !Number.isNaN(Number(v)), message)

const requiredNumberString = (message = 'Required') =>
  z
    .string()
    .min(1, 'Required')
    .refine((v) => !Number.isNaN(Number(v)), message)

const percentageString = requiredNumberString('Must be between 0 and 100').refine(
  (v) => Number(v) >= 0 && Number(v) <= 100,
  'Must be between 0 and 100',
)

const addressSchema = z.object({
  line1: z.string().trim().min(1, 'Required'),
  line2: z.string().trim().optional().or(z.literal('')),
  city: z.string().trim().min(1, 'Required'),
  state: z.string().trim().min(1, 'Required'),
  postalCode: z.string().trim().min(1, 'Required'),
  country: z.string().trim().min(1, 'Required'),
})

const namePattern = z.string().trim().min(1, 'Required').max(100, 'Must be at most 100 characters')

export const applicantSchema = z.object({
  legalFirstName: namePattern,
  legalMiddleName: z.string().trim().max(100).optional().or(z.literal('')),
  legalLastName: namePattern,
  dateOfBirth: z
    .string()
    .min(1, 'Required')
    .refine((v) => new Date(v) < new Date(), 'Must be in the past'),
  residentialAddress: addressSchema,
  email: z.string().trim().email('Must be a valid email address'),
  phone: z
    .string()
    .trim()
    .regex(/^[0-9+()\-\s]{7,20}$/, '7-20 characters of digits, spaces, +, -, ( or )'),
  roleTitle: z.string().trim().min(1, 'Required'),
  ownershipPercentage: numberString('Must be between 0 and 100')
    .refine((v) => v.trim() === '' || (Number(v) >= 0 && Number(v) <= 100), 'Must be between 0 and 100')
    .optional()
    .or(z.literal('')),
  governmentIdType: z.enum(['DriversLicense', 'Passport', 'NationalId']),
  governmentIdNumber: z.string().trim().min(4, 'Must be at least 4 characters'),
  consentAccepted: z.boolean().refine((v) => v === true, 'Consent is required to continue'),
})

export type ApplicantFormValues = z.infer<typeof applicantSchema>

const volumeProfileSchema = z
  .object({
    expectedAnnualCardVolume: requiredNumberString('Must not be negative').refine((v) => Number(v) >= 0, 'Must not be negative'),
    averageTicket: requiredNumberString('Must not be negative').refine((v) => Number(v) >= 0, 'Must not be negative'),
    highestTicket: requiredNumberString('Must not be negative').refine((v) => Number(v) >= 0, 'Must not be negative'),
    monthlyTransactionCount: requiredNumberString('Must not be negative').refine((v) => Number(v) >= 0, 'Must not be negative'),
    cardPresentPercentage: percentageString,
    ecommercePercentage: percentageString,
  })
  .refine((v) => Number(v.highestTicket) >= Number(v.averageTicket), {
    message: 'Highest ticket must not be less than average ticket',
    path: ['highestTicket'],
  })

export const businessSchema = z.object({
  legalBusinessName: z.string().trim().min(1, 'Required').max(200),
  dbaName: z.string().trim().max(200).optional().or(z.literal('')),
  entityType: z.enum(['Llc', 'Corporation', 'Partnership', 'SoleProprietor', 'Nonprofit', 'Other']),
  formationCountry: z.string().trim().min(1, 'Required').max(200),
  formationState: z.string().trim().max(200).optional().or(z.literal('')),
  registrationIdentifierType: z.enum(['Ein', 'StateUbi', 'Other']),
  registrationIdentifierValue: z.string().trim().min(4, 'Must be at least 4 characters'),
  registeredAddress: addressSchema,
  operatingAddress: addressSchema,
  websiteUrl: z
    .string()
    .trim()
    .url('Must be a valid absolute http(s) URL')
    .optional()
    .or(z.literal('')),
  businessDescription: z.string().trim().min(1, 'Required').max(2000),
  businessStartDate: z
    .string()
    .min(1, 'Required')
    .refine((v) => new Date(v) <= new Date(), 'Must not be in the future'),
  volumeProfile: volumeProfileSchema,
  settlementAccountHolder: z.string().trim().min(1, 'Required'),
  settlementBankName: z.string().trim().min(1, 'Required'),
  settlementAccountNumber: z.string().trim().min(4, 'Must be at least 4 characters'),
  settlementStatementDate: z.string().min(1, 'Required'),
  existingProcessor: z.string().trim().max(200).optional().or(z.literal('')),
})

export type BusinessFormValues = z.infer<typeof businessSchema>
