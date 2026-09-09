import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, ApiError, sha256Base64, uploadToPresignedUrl } from '@/lib/api-client'
import type { DocumentType } from '@/lib/types'
import { toast } from '@/stores/toast-store'

const keys = {
  application: (id: string) => ['application', id] as const,
  documents: (id: string) => ['documents', id] as const,
  classification: (id: string) => ['classification', id] as const,
  evaluation: (id: string) => ['evaluation', id] as const,
  mcc: (query: string) => ['mcc', query] as const,
}

export function useDocuments(id: string | undefined) {
  return useQuery({
    queryKey: keys.documents(id ?? ''),
    queryFn: () => api.listDocuments(id!),
    enabled: Boolean(id),
  })
}

export function useApplication(id: string | undefined) {
  return useQuery({
    queryKey: keys.application(id ?? ''),
    queryFn: () => api.getApplication(id!),
    enabled: Boolean(id),
  })
}

export function usePatchApplicant(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => api.patchApplicant(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.application(id) })
    },
    onError: (error) => reportError(error, 'Could not save applicant details'),
  })
}

export function usePatchBusiness(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => api.patchBusiness(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.application(id) })
    },
    onError: (error) => reportError(error, 'Could not save business details'),
  })
}

export function useUploadDocument(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({
      type,
      file,
      onProgress,
    }: {
      type: DocumentType
      file: File
      onProgress: (percent: number) => void
    }) => {
      const checksum = await sha256Base64(file)
      const presigned = await api.presignDocument(id, {
        type,
        originalFilename: file.name,
        contentType: file.type || 'application/octet-stream',
        declaredSizeBytes: file.size,
        declaredChecksumSha256Base64: checksum,
      })
      await uploadToPresignedUrl(presigned.uploadUrl, presigned.uploadFields, file, onProgress)
      return api.completeDocument(id, presigned.document.id)
    },
    onSuccess: (document) => {
      queryClient.invalidateQueries({ queryKey: keys.application(id) })
      queryClient.invalidateQueries({ queryKey: keys.documents(id) })
      if (document.status === 'Accepted' || document.status === 'Received') {
        toast.success(`${document.originalFilename} uploaded`, 'Verified and on file.')
      } else if (document.status === 'Rejected') {
        toast.error(`${document.originalFilename} was rejected`, document.rejectionReason ?? undefined)
      }
    },
    onError: (error) => reportError(error, 'Upload failed'),
  })
}

export function useClassify(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.classify(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.classification(id) })
    },
    onError: (error) => reportError(error, 'Classification failed'),
  })
}

export function useClassification(id: string | undefined) {
  return useQuery({
    queryKey: keys.classification(id ?? ''),
    queryFn: () => api.getClassification(id!),
    enabled: Boolean(id),
    retry: false,
  })
}

export function useConfirmClassification(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (mccCode: string) => api.confirmClassification(id, mccCode),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.classification(id) })
      toast.success('MCC confirmed')
    },
    onError: (error) => reportError(error, 'Could not confirm the MCC code'),
  })
}

export function useMccSearch(query: string) {
  return useQuery({
    queryKey: keys.mcc(query),
    queryFn: () => api.searchMcc(query),
    enabled: query.length > 0,
  })
}

export function useEvaluate(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (processingStatementDocumentId?: string | null) =>
      api.evaluate(id, processingStatementDocumentId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.evaluation(id) })
    },
    onError: (error) => reportError(error, 'Evaluation failed'),
  })
}

export function useEvaluation(id: string | undefined) {
  return useQuery({
    queryKey: keys.evaluation(id ?? ''),
    queryFn: () => api.getEvaluation(id!),
    enabled: Boolean(id),
    retry: false,
  })
}

export function useSubmitApplication(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.submit(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.application(id) })
    },
    onError: (error) => {
      if (error instanceof ApiError && error.code === 'VALIDATION_FAILED') {
        toast.error('Not ready to submit', 'Some required items are still missing.')
        return
      }
      reportError(error, 'Submission failed')
    },
  })
}

function reportError(error: unknown, fallbackTitle: string) {
  if (error instanceof ApiError) {
    toast.error(fallbackTitle, error.message)
  } else {
    toast.error(fallbackTitle, 'An unexpected error occurred.')
  }
}
