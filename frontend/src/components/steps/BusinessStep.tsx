import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'

import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { Field } from '@/components/shared/Field'
import { StepFooter } from '@/components/shared/StepFooter'
import { StepHeader } from '@/components/shared/StepHeader'
import { usePatchBusiness } from '@/hooks/use-application'
import { businessSchema, type BusinessFormValues } from '@/lib/validation'
import type { ApplicationDetail } from '@/lib/types'
import { toast } from '@/stores/toast-store'

export function BusinessStep({ application }: { application: ApplicationDetail }) {
  const navigate = useNavigate()
  const patchBusiness = usePatchBusiness(application.id)
  const b = application.business

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
  } = useForm<BusinessFormValues>({
    resolver: zodResolver(businessSchema),
    defaultValues: {
      legalBusinessName: b?.legalBusinessName ?? '',
      dbaName: b?.dbaName ?? '',
      entityType: b?.entityType ?? 'Llc',
      formationCountry: b?.formationCountry ?? '',
      formationState: b?.formationState ?? '',
      registrationIdentifierType: b?.registrationIdentifier?.type ?? 'Ein',
      registrationIdentifierValue: '',
      registeredAddress: {
        line1: b?.registeredAddress?.line1 ?? '',
        line2: b?.registeredAddress?.line2 ?? '',
        city: b?.registeredAddress?.city ?? '',
        state: b?.registeredAddress?.state ?? '',
        postalCode: b?.registeredAddress?.postalCode ?? '',
        country: b?.registeredAddress?.country ?? '',
      },
      operatingAddress: {
        line1: b?.operatingAddress?.line1 ?? '',
        line2: b?.operatingAddress?.line2 ?? '',
        city: b?.operatingAddress?.city ?? '',
        state: b?.operatingAddress?.state ?? '',
        postalCode: b?.operatingAddress?.postalCode ?? '',
        country: b?.operatingAddress?.country ?? '',
      },
      websiteUrl: b?.websiteUrl ?? '',
      businessDescription: b?.businessDescription ?? '',
      businessStartDate: b?.businessStartDate ?? '',
      volumeProfile: {
        expectedAnnualCardVolume: b?.volumeProfile ? String(b.volumeProfile.expectedAnnualCardVolume) : '',
        averageTicket: b?.volumeProfile ? String(b.volumeProfile.averageTicket) : '',
        highestTicket: b?.volumeProfile ? String(b.volumeProfile.highestTicket) : '',
        monthlyTransactionCount: b?.volumeProfile ? String(b.volumeProfile.monthlyTransactionCount) : '',
        cardPresentPercentage: b?.volumeProfile ? String(b.volumeProfile.cardPresentPercentage) : '',
        ecommercePercentage: b?.volumeProfile ? String(b.volumeProfile.ecommercePercentage) : '',
      },
      settlementAccountHolder: b?.settlementBankAccount?.accountHolder ?? '',
      settlementBankName: b?.settlementBankAccount?.bankName ?? '',
      settlementAccountNumber: '',
      settlementStatementDate: b?.settlementBankAccount?.statementDate ?? '',
      existingProcessor: b?.existingProcessor ?? '',
    },
  })

  const entityType = watch('entityType')
  const registrationIdentifierType = watch('registrationIdentifierType')

  const onSubmit = handleSubmit(async (values) => {
    try {
      await patchBusiness.mutateAsync({
        legalBusinessName: values.legalBusinessName,
        dbaName: values.dbaName || null,
        entityType: values.entityType,
        formationCountry: values.formationCountry,
        formationState: values.formationState || null,
        registrationIdentifier: {
          type: values.registrationIdentifierType,
          value: values.registrationIdentifierValue,
        },
        registeredAddress: values.registeredAddress,
        operatingAddress: values.operatingAddress,
        websiteUrl: values.websiteUrl || null,
        businessDescription: values.businessDescription,
        businessStartDate: values.businessStartDate,
        volumeProfile: {
          expectedAnnualCardVolume: Number(values.volumeProfile.expectedAnnualCardVolume),
          averageTicket: Number(values.volumeProfile.averageTicket),
          highestTicket: Number(values.volumeProfile.highestTicket),
          monthlyTransactionCount: Number(values.volumeProfile.monthlyTransactionCount),
          cardPresentPercentage: Number(values.volumeProfile.cardPresentPercentage),
          ecommercePercentage: Number(values.volumeProfile.ecommercePercentage),
        },
        settlementBankAccount: {
          accountHolder: values.settlementAccountHolder,
          bankName: values.settlementBankName,
          accountNumber: values.settlementAccountNumber,
          statementDate: values.settlementStatementDate,
        },
        existingProcessor: values.existingProcessor || null,
      })
      toast.success('Business details saved')
      navigate(`/applications/${application.id}/documents`)
    } catch {
      // usePatchBusiness already surfaces a toast on failure.
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <StepHeader
        eyebrow="Step 2 of 6"
        title="About the business"
        description="Legal entity, registration, and expected processing profile. This also feeds MCC classification later, so a clear description helps."
      />

      <div className="space-y-8">
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <Field label="Legal business name" error={errors.legalBusinessName?.message}>
            <Input {...register('legalBusinessName')} placeholder="Acme Trading LLC" />
          </Field>
          <Field label="DBA / trade name" hint="Optional">
            <Input {...register('dbaName')} placeholder="Acme" />
          </Field>
        </div>

        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <Field label="Entity type">
            <Select value={entityType} onValueChange={(v) => setValue('entityType', v as BusinessFormValues['entityType'])}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Llc">LLC</SelectItem>
                <SelectItem value="Corporation">Corporation</SelectItem>
                <SelectItem value="Partnership">Partnership</SelectItem>
                <SelectItem value="SoleProprietor">Sole proprietor</SelectItem>
                <SelectItem value="Nonprofit">Nonprofit</SelectItem>
                <SelectItem value="Other">Other</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field label="Website" error={errors.websiteUrl?.message} hint="Optional">
            <Input {...register('websiteUrl')} placeholder="https://acme.com" />
          </Field>
        </div>

        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <Field label="Formation country" error={errors.formationCountry?.message}>
            <Input {...register('formationCountry')} placeholder="US" />
          </Field>
          <Field label="Formation state / province" hint="Optional">
            <Input {...register('formationState')} placeholder="DE" />
          </Field>
        </div>

        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <Field label="Registration identifier type">
            <Select
              value={registrationIdentifierType}
              onValueChange={(v) =>
                setValue('registrationIdentifierType', v as BusinessFormValues['registrationIdentifierType'])
              }
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Ein">EIN</SelectItem>
                <SelectItem value="StateUbi">State UBI</SelectItem>
                <SelectItem value="Other">Other</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field
            label="Identifier value"
            error={errors.registrationIdentifierValue?.message}
            hint={b?.registrationIdentifier ? `On file: ${b.registrationIdentifier.maskedValue}` : undefined}
          >
            <Input {...register('registrationIdentifierValue')} placeholder={b?.registrationIdentifier ? 'Enter to replace' : ''} />
          </Field>
        </div>

        <div className="space-y-3">
          <p className="text-sm font-medium text-foreground">Registered address</p>
          <AddressGrid register={register} prefix="registeredAddress" errors={errors.registeredAddress} />
        </div>
        <div className="space-y-3">
          <p className="text-sm font-medium text-foreground">Operating address</p>
          <AddressGrid register={register} prefix="operatingAddress" errors={errors.operatingAddress} />
        </div>

        <Field label="Business description" error={errors.businessDescription?.message} hint="What does the business sell? Used for MCC classification.">
          <Textarea {...register('businessDescription')} rows={3} placeholder="A neighborhood grocery store selling..." />
        </Field>

        <Field label="Business start date" error={errors.businessStartDate?.message}>
          <Input type="date" {...register('businessStartDate')} className="max-w-xs" />
        </Field>

        <div className="space-y-3">
          <p className="text-sm font-medium text-foreground">Processing volume profile</p>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="Expected annual card volume ($)" error={errors.volumeProfile?.expectedAnnualCardVolume?.message}>
              <Input type="number" step="0.01" {...register('volumeProfile.expectedAnnualCardVolume')} />
            </Field>
            <Field label="Average ticket ($)" error={errors.volumeProfile?.averageTicket?.message}>
              <Input type="number" step="0.01" {...register('volumeProfile.averageTicket')} />
            </Field>
            <Field label="Highest ticket ($)" error={errors.volumeProfile?.highestTicket?.message}>
              <Input type="number" step="0.01" {...register('volumeProfile.highestTicket')} />
            </Field>
            <Field label="Monthly transaction count" error={errors.volumeProfile?.monthlyTransactionCount?.message}>
              <Input type="number" {...register('volumeProfile.monthlyTransactionCount')} />
            </Field>
            <Field label="Card-present %" error={errors.volumeProfile?.cardPresentPercentage?.message}>
              <Input type="number" step="0.01" {...register('volumeProfile.cardPresentPercentage')} />
            </Field>
            <Field label="E-commerce %" error={errors.volumeProfile?.ecommercePercentage?.message}>
              <Input type="number" step="0.01" {...register('volumeProfile.ecommercePercentage')} />
            </Field>
          </div>
        </div>

        <div className="space-y-3">
          <p className="text-sm font-medium text-foreground">Settlement bank account</p>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="Account holder" error={errors.settlementAccountHolder?.message}>
              <Input {...register('settlementAccountHolder')} />
            </Field>
            <Field label="Bank name" error={errors.settlementBankName?.message}>
              <Input {...register('settlementBankName')} />
            </Field>
            <Field
              label="Account number"
              error={errors.settlementAccountNumber?.message}
              hint={b?.settlementBankAccount ? `On file, ending in ${b.settlementBankAccount.last4}` : 'Only the last 4 digits are ever stored'}
            >
              <Input {...register('settlementAccountNumber')} placeholder={b?.settlementBankAccount ? 'Enter to replace' : ''} />
            </Field>
            <Field label="Statement date" error={errors.settlementStatementDate?.message}>
              <Input type="date" {...register('settlementStatementDate')} />
            </Field>
          </div>
        </div>

        <Field label="Existing payment processor" hint="Optional">
          <Input {...register('existingProcessor')} placeholder="e.g. Stripe, Square..." />
        </Field>
      </div>

      <StepFooter
        continueType="submit"
        loading={patchBusiness.isPending}
        onBack={() => navigate(`/applications/${application.id}/applicant`)}
      />
    </form>
  )
}

function AddressGrid({
  register,
  prefix,
  errors,
}: {
  register: ReturnType<typeof useForm<BusinessFormValues>>['register']
  prefix: 'registeredAddress' | 'operatingAddress'
  errors?: {
    line1?: { message?: string }
    city?: { message?: string }
    state?: { message?: string }
    postalCode?: { message?: string }
    country?: { message?: string }
  }
}) {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <Field label="Address line 1" error={errors?.line1?.message}>
        <Input {...register(`${prefix}.line1`)} />
      </Field>
      <Field label="Address line 2" hint="Optional">
        <Input {...register(`${prefix}.line2`)} />
      </Field>
      <Field label="City" error={errors?.city?.message}>
        <Input {...register(`${prefix}.city`)} />
      </Field>
      <Field label="State / province" error={errors?.state?.message}>
        <Input {...register(`${prefix}.state`)} />
      </Field>
      <Field label="Postal code" error={errors?.postalCode?.message}>
        <Input {...register(`${prefix}.postalCode`)} />
      </Field>
      <Field label="Country" error={errors?.country?.message}>
        <Input {...register(`${prefix}.country`)} placeholder="US" />
      </Field>
    </div>
  )
}
