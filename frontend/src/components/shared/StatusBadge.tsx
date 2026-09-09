import { Badge } from '@/components/ui/badge'
import type { DocumentStatus } from '@/lib/types'

const STATUS_VARIANT: Record<DocumentStatus, 'success' | 'warning' | 'destructive' | 'secondary'> = {
  Requested: 'secondary',
  Uploading: 'warning',
  Received: 'warning',
  Processing: 'warning',
  Accepted: 'success',
  NeedsReview: 'warning',
  Rejected: 'destructive',
}

const STATUS_LABEL: Record<DocumentStatus, string> = {
  Requested: 'Requested',
  Uploading: 'Uploading',
  Received: 'Received',
  Processing: 'Processing',
  Accepted: 'Accepted',
  NeedsReview: 'Needs review',
  Rejected: 'Rejected',
}

export function DocumentStatusBadge({ status }: { status: DocumentStatus }) {
  return <Badge variant={STATUS_VARIANT[status]}>{STATUS_LABEL[status]}</Badge>
}
