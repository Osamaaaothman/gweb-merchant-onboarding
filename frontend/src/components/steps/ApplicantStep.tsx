import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'

import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Field } from '@/components/shared/Field'
import { StepFooter } from '@/components/shared/StepFooter'
import { StepHeader } from '@/components/shared/StepHeader'
import { usePatchApplicant } from '@/hooks/use-application'
import { applicantSchema, type ApplicantFormValues } from '@/lib/validation'
import type { ApplicationDetail } from '@/lib/types'
import { toast } from '@/stores/toast-store'

export function ApplicantStep({ application }: { application: ApplicationDetail }) {
  const navigate = useNavigate()
  const patchApplicant = usePatchApplicant(application.id)
  const a = application.applicant

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
  } = useForm<ApplicantFormValues>({
    resolver: zodResolver(applicantSchema),
    defaultValues: {
      legalFirstName: a?.legalFirstName ?? '',
      legalMiddleName: a?.legalMiddleName ?? '',
      legalLastName: a?.legalLastName ?? '',
      dateOfBirth: a?.dateOfBirth ?? '',
      residentialAddress: {
        line1: a?.residentialAddress?.line1 ?? '',
        line2: a?.residentialAddress?.line2 ?? '',
        city: a?.residentialAddress?.city ?? '',
        state: a?.residentialAddress?.state ?? '',
        postalCode: a?.residentialAddress?.postalCode ?? '',
        country: a?.residentialAddress?.country ?? '',
      },
      email: a?.email ?? '',
      phone: a?.phone ?? '',
      roleTitle: a?.roleTitle ?? '',
      ownershipPercentage: a?.ownershipPercentage != null ? String(a.ownershipPercentage) : '',
      governmentIdType: a?.governmentId?.type ?? 'Passport',
      governmentIdNumber: '',
      consentAccepted: Boolean(a?.consentVersion),
    },
  })

  const governmentIdType = watch('governmentIdType')
  const consentAccepted = watch('consentAccepted')

  const onSubmit = handleSubmit(async (values) => {
    try {
      await patchApplicant.mutateAsync({
        legalFirstName: values.legalFirstName,
        legalMiddleName: values.legalMiddleName || null,
        legalLastName: values.legalLastName,
        dateOfBirth: values.dateOfBirth,
        residentialAddress: values.residentialAddress,
        email: values.email,
        phone: values.phone,
        roleTitle: values.roleTitle,
        ownershipPercentage: values.ownershipPercentage ? Number(values.ownershipPercentage) : null,
        governmentId: { type: values.governmentIdType, number: values.governmentIdNumber },
        consentVersion: 'v1.0',
      })
      toast.success('Applicant details saved')
      navigate(`/applications/${application.id}/business`)
    } catch {
      // usePatchApplicant already surfaces a toast on failure.
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <StepHeader
        eyebrow="Step 1 of 6"
        title="Tell us about yourself"
        description="This information identifies the primary applicant / control person for the business. It's saved as you go, so you can pick up right where you left off."
      />

      <div className="space-y-8">
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-3">
          <Field label="Legal first name" error={errors.legalFirstName?.message}>
            <Input {...register('legalFirstName')} placeholder="Jane" />
          </Field>
          <Field label="Middle name" error={errors.legalMiddleName?.message} hint="Optional">
            <Input {...register('legalMiddleName')} placeholder="Marie" />
          </Field>
          <Field label="Legal last name" error={errors.legalLastName?.message}>
            <Input {...register('legalLastName')} placeholder="Doe" />
          </Field>
        </div>

        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <Field label="Date of birth" error={errors.dateOfBirth?.message}>
            <Input type="date" {...register('dateOfBirth')} />
          </Field>
          <Field label="Role / title in the business" error={errors.roleTitle?.message}>
            <Input {...register('roleTitle')} placeholder="CEO, Owner, Managing Partner..." />
          </Field>
        </div>

        <div className="space-y-3">
          <p className="text-sm font-medium text-foreground">Residential address</p>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="Address line 1" error={errors.residentialAddress?.line1?.message}>
              <Input {...register('residentialAddress.line1')} placeholder="1 Main St" />
            </Field>
            <Field label="Address line 2" hint="Optional">
              <Input {...register('residentialAddress.line2')} placeholder="Apt, suite..." />
            </Field>
            <Field label="City" error={errors.residentialAddress?.city?.message}>
              <Input {...register('residentialAddress.city')} />
            </Field>
            <Field label="State / province" error={errors.residentialAddress?.state?.message}>
              <Input {...register('residentialAddress.state')} />
            </Field>
            <Field label="Postal code" error={errors.residentialAddress?.postalCode?.message}>
              <Input {...register('residentialAddress.postalCode')} />
            </Field>
            <Field label="Country" error={errors.residentialAddress?.country?.message}>
              <Input {...register('residentialAddress.country')} placeholder="US" />
            </Field>
          </div>
        </div>

        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <Field label="Email" error={errors.email?.message}>
            <Input type="email" {...register('email')} placeholder="jane@business.com" />
          </Field>
          <Field label="Phone" error={errors.phone?.message}>
            <Input type="tel" {...register('phone')} placeholder="+1 555 000 0000" />
          </Field>
        </div>

        <Field label="Ownership percentage" error={errors.ownershipPercentage?.message} hint="If applicable, 0-100">
          <Input type="number" step="0.01" {...register('ownershipPercentage')} placeholder="e.g. 45" />
        </Field>

        <div className="space-y-3">
          <p className="text-sm font-medium text-foreground">Government-issued identification</p>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="ID type">
              <Select
                value={governmentIdType}
                onValueChange={(v) => setValue('governmentIdType', v as ApplicantFormValues['governmentIdType'])}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Passport">Passport</SelectItem>
                  <SelectItem value="DriversLicense">Driver's license</SelectItem>
                  <SelectItem value="NationalId">National ID</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field
              label="ID number"
              error={errors.governmentIdNumber?.message}
              hint={a?.governmentId ? `On file, ending in ${a.governmentId.last4}` : 'Only the last 4 digits are ever stored'}
            >
              <Input {...register('governmentIdNumber')} placeholder={a?.governmentId ? 'Enter to replace' : ''} />
            </Field>
          </div>
        </div>

        <label className="flex cursor-pointer items-start gap-3 rounded-lg border border-border bg-secondary/40 p-4">
          <Checkbox
            checked={consentAccepted}
            onCheckedChange={(v) => setValue('consentAccepted', v === true, { shouldValidate: true })}
            className="mt-0.5"
          />
          <span className="text-sm text-muted-foreground">
            I confirm the information provided is accurate and I accept the{' '}
            <span className="font-medium text-foreground">Terms of Service (v1.0)</span> on behalf of
            the applicant.
          </span>
        </label>
        {errors.consentAccepted && (
          <p className="-mt-4 text-xs font-medium text-destructive">{errors.consentAccepted.message}</p>
        )}
      </div>

      <StepFooter continueType="submit" loading={patchApplicant.isPending} />
    </form>
  )
}
