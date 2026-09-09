import { AnimatePresence, motion } from 'motion/react'
import { useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { CheckCircle2, FileText, Loader2, Upload } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'
import { DocumentStatusBadge } from '@/components/shared/StatusBadge'
import { StepFooter } from '@/components/shared/StepFooter'
import { StepHeader } from '@/components/shared/StepHeader'
import { useDocuments, useUploadDocument } from '@/hooks/use-application'
import { cn } from '@/lib/utils'
import { DOCUMENT_TYPE_LABELS, REQUIRED_DOCUMENT_TYPES, type DocumentType, type DocumentView } from '@/lib/types'
import type { ApplicationDetail } from '@/lib/types'

const OPTIONAL_DOCUMENT_TYPES: DocumentType[] = ['BusinessLicense', 'ProcessingStatement', 'AdditionalEvidence']

const SATISFIED_STATUSES: DocumentView['status'][] = ['Received', 'Accepted']

export function DocumentsStep({ application }: { application: ApplicationDetail }) {
  const navigate = useNavigate()
  const { data: documents } = useDocuments(application.id)

  const latestByType = new Map<DocumentType, DocumentView>()
  for (const doc of documents ?? []) {
    const existing = latestByType.get(doc.type)
    if (!existing || (doc.uploadedAt ?? '') >= (existing.uploadedAt ?? '')) {
      latestByType.set(doc.type, doc)
    }
  }

  const requiredSatisfied = REQUIRED_DOCUMENT_TYPES.every((type) =>
    SATISFIED_STATUSES.includes(latestByType.get(type)?.status as DocumentView['status']),
  )

  return (
    <div>
      <StepHeader
        eyebrow="Step 3 of 6"
        title="Upload documents"
        description="Government ID, business registration, and bank evidence are required before you can submit. Files upload directly and never pass through this server."
      />

      <div className="space-y-3">
        {REQUIRED_DOCUMENT_TYPES.map((type) => (
          <DocumentCard key={type} applicationId={application.id} type={type} document={latestByType.get(type)} required />
        ))}
      </div>

      <div className="mt-8">
        <p className="mb-3 text-xs font-medium tracking-wide text-muted-foreground uppercase">
          Optional / conditional
        </p>
        <div className="space-y-3">
          {OPTIONAL_DOCUMENT_TYPES.map((type) => (
            <DocumentCard key={type} applicationId={application.id} type={type} document={latestByType.get(type)} />
          ))}
        </div>
      </div>

      <StepFooter
        onBack={() => navigate(`/applications/${application.id}/business`)}
        onContinue={() => navigate(`/applications/${application.id}/classification`)}
        continueDisabled={!requiredSatisfied}
        continueLabel={requiredSatisfied ? 'Continue' : 'Upload required documents to continue'}
      />
    </div>
  )
}

function DocumentCard({
  applicationId,
  type,
  document,
  required,
}: {
  applicationId: string
  type: DocumentType
  document?: DocumentView
  required?: boolean
}) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [progress, setProgress] = useState(0)
  const upload = useUploadDocument(applicationId)

  const isSatisfied = document && SATISFIED_STATUSES.includes(document.status)
  const isRejected = document?.status === 'Rejected'

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    setProgress(0)
    upload.mutate({ type, file, onProgress: setProgress })
  }

  return (
    <motion.div
      layout
      className={cn(
        'flex flex-col gap-3 rounded-xl border bg-card p-4 transition-colors sm:flex-row sm:items-center sm:gap-4',
        isSatisfied ? 'border-success/40' : isRejected ? 'border-destructive/40' : 'border-border',
      )}
    >
      <div className="flex min-w-0 flex-1 items-center gap-4">
        <div
          className={cn(
            'flex size-11 shrink-0 items-center justify-center rounded-lg',
            isSatisfied ? 'bg-success/10 text-success' : 'bg-secondary text-muted-foreground',
          )}
        >
          {isSatisfied ? <CheckCircle2 className="size-5" /> : <FileText className="size-5" />}
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <p className="text-sm font-medium text-foreground">{DOCUMENT_TYPE_LABELS[type]}</p>
            {required && !document && (
              <span className="text-[11px] font-medium text-muted-foreground">Required</span>
            )}
          </div>
          {document ? (
            <p className="mt-0.5 truncate text-xs text-muted-foreground">{document.originalFilename}</p>
          ) : (
            <p className="mt-0.5 text-xs text-muted-foreground">PDF, JPG or PNG</p>
          )}
          {isRejected && document?.rejectionReason && (
            <p className="mt-1 text-xs font-medium text-destructive">{document.rejectionReason}</p>
          )}

          <AnimatePresence>
            {upload.isPending && (
              <motion.div
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: 'auto' }}
                exit={{ opacity: 0, height: 0 }}
                className="mt-2"
              >
                <Progress value={progress} className="h-1.5" />
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>

      <div className="flex shrink-0 items-center justify-end gap-3">
        {document && <DocumentStatusBadge status={document.status} />}
        <input ref={inputRef} type="file" accept=".pdf,.jpg,.jpeg,.png" className="hidden" onChange={handleFileChange} />
        <Button
          type="button"
          variant={document ? 'outline' : 'secondary'}
          size="sm"
          disabled={upload.isPending}
          onClick={() => inputRef.current?.click()}
        >
          {upload.isPending ? (
            <Loader2 className="size-3.5 animate-spin" />
          ) : (
            <Upload className="size-3.5" />
          )}
          {document ? 'Replace' : 'Upload'}
        </Button>
      </div>
    </motion.div>
  )
}
