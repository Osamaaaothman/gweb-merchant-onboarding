import type {
  ApiErrorBody,
  ApplicationDetail,
  ApplicationSummary,
  DocumentType,
  DocumentView,
  Evaluation,
  McClassification,
  McSearchResult,
  PresignedUpload,
  SubmitResult,
} from '@/lib/types'

/** Thrown for every non-2xx response. Carries the parsed structured error body
 * (HttpErrorMapper's shape) so callers can branch on `code` or render `details`
 * (e.g. SubmissionReadiness) without re-parsing anything. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly details: unknown
  readonly correlationId: string | null

  constructor(status: number, body: ApiErrorBody) {
    super(body.error.message)
    this.name = 'ApiError'
    this.status = status
    this.code = body.error.code
    this.details = body.error.details
    this.correlationId = body.correlationId
  }
}

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'x-actor': 'frontend-user',
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const body = (await response.json()) as ApiErrorBody
    throw new ApiError(response.status, body)
  }

  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

export const api = {
  createApplication: () => request<ApplicationSummary>('/v1/applications', { method: 'POST' }),

  getApplication: (id: string) => request<ApplicationDetail>(`/v1/applications/${id}`),

  patchApplicant: (id: string, body: Record<string, unknown>) =>
    request(`/v1/applications/${id}/applicant`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  patchBusiness: (id: string, body: Record<string, unknown>) =>
    request(`/v1/applications/${id}/business`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  presignDocument: (
    id: string,
    body: {
      type: DocumentType
      originalFilename: string
      contentType: string
      declaredSizeBytes: number
      declaredChecksumSha256Base64: string
    },
  ) =>
    request<PresignedUpload>(`/v1/applications/${id}/documents/presign`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  completeDocument: (id: string, documentId: string) =>
    request<DocumentView>(`/v1/applications/${id}/documents/${documentId}/complete`, {
      method: 'POST',
    }),

  getDocument: (id: string, documentId: string) =>
    request<DocumentView>(`/v1/applications/${id}/documents/${documentId}`),

  listDocuments: (id: string) => request<DocumentView[]>(`/v1/applications/${id}/documents`),

  searchMcc: (query: string) =>
    request<McSearchResult>(`/v1/mcc?query=${encodeURIComponent(query)}`),

  classify: (id: string) =>
    request<McClassification>(`/v1/applications/${id}/classify`, { method: 'POST' }),

  getClassification: (id: string) =>
    request<McClassification>(`/v1/applications/${id}/classify`),

  confirmClassification: (id: string, mccCode: string) =>
    request<McClassification>(`/v1/applications/${id}/classify/confirm`, {
      method: 'POST',
      body: JSON.stringify({ mccCode }),
    }),

  evaluate: (id: string, processingStatementDocumentId?: string | null) =>
    request<Evaluation>(`/v1/applications/${id}/evaluate`, {
      method: 'POST',
      body: JSON.stringify({ processingStatementDocumentId: processingStatementDocumentId ?? null }),
    }),

  getEvaluation: (id: string) => request<Evaluation>(`/v1/applications/${id}/evaluation`),

  submit: (id: string) => request<SubmitResult>(`/v1/applications/${id}/submit`, { method: 'POST' }),
}

/** Uploads a file straight to the pre-signed destination the presign step returned --
 * multipart/form-data with every uploadFields entry set before `file`, the exact S3
 * presigned-POST contract (src/Gweb.Adapters.Storage/S3DocumentStorage.cs). Reports
 * progress via XHR (fetch has no upload-progress event), which is what makes the
 * upload-progress bar in DocumentsStep real, not simulated. */
/** Local dev only: rewrites a same-machine MinIO presigned URL to a same-origin path
 * so the browser's upload goes through the Vite dev proxy instead of a cross-origin
 * request straight to MinIO -- see vite.config.ts's proxy comment for why (this
 * MinIO version's bucket CORS configuration API doesn't work). A real deployed
 * frontend talking to real S3 (or MinIO with working CORS) would never hit this --
 * the URL simply wouldn't match. */
function toSameOriginUploadUrl(uploadUrl: string): string {
  const local = /^https?:\/\/(localhost|127\.0\.0\.1):9000(\/.*)$/.exec(uploadUrl)
  return local ? local[2] : uploadUrl
}

export function uploadToPresignedUrl(
  uploadUrl: string,
  uploadFields: Record<string, string>,
  file: File,
  onProgress: (percent: number) => void,
): Promise<void> {
  return new Promise((resolve, reject) => {
    const formData = new FormData()
    for (const [key, value] of Object.entries(uploadFields)) {
      formData.append(key, value)
    }
    formData.append('file', file)

    const xhr = new XMLHttpRequest()
    xhr.open('POST', toSameOriginUploadUrl(uploadUrl))
    xhr.upload.addEventListener('progress', (event) => {
      if (event.lengthComputable) {
        onProgress(Math.round((event.loaded / event.total) * 100))
      }
    })
    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        onProgress(100)
        resolve()
      } else {
        reject(new Error(`Upload failed with status ${xhr.status}`))
      }
    })
    xhr.addEventListener('error', () => reject(new Error('Upload failed -- network error')))
    xhr.send(formData)
  })
}

export async function sha256Base64(file: File): Promise<string> {
  const buffer = await file.arrayBuffer()
  const hash = await crypto.subtle.digest('SHA-256', buffer)
  return btoa(String.fromCharCode(...new Uint8Array(hash)))
}
