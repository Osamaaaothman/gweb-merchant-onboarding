import { motion } from 'motion/react'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { ArrowRight, Loader2, ShieldCheck } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { api, ApiError } from '@/lib/api-client'
import { toast } from '@/stores/toast-store'

export function LandingPage() {
  const navigate = useNavigate()
  const [resumeId, setResumeId] = useState('')
  const [starting, setStarting] = useState(false)
  const [resuming, setResuming] = useState(false)

  const startNew = async () => {
    setStarting(true)
    try {
      const application = await api.createApplication()
      navigate(`/applications/${application.id}/applicant`)
    } catch {
      toast.error('Could not start a new application')
    } finally {
      setStarting(false)
    }
  }

  const resume = async () => {
    const id = resumeId.trim()
    if (!id) return
    setResuming(true)
    try {
      await api.getApplication(id)
      navigate(`/applications/${id}/applicant`)
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        toast.error('No application found with that ID')
      } else {
        toast.error('Could not resume that application')
      }
    } finally {
      setResuming(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background px-6 py-16">
      <motion.div
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
        className="w-full max-w-md"
      >
        <div className="mb-10 text-center">
          <div className="mx-auto mb-5 flex size-12 items-center justify-center rounded-2xl bg-primary text-primary-foreground shadow-lg shadow-primary/20">
            <ShieldCheck className="size-6" />
          </div>
          <p className="text-xs font-medium tracking-widest text-primary uppercase">GWEB</p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight text-foreground">
            Merchant Onboarding
          </h1>
          <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
            A guided intake for underwriting review -- applicant details, business
            profile, documents, and classification, all in one place.
          </p>
        </div>

        <Card className="gap-0 p-6">
          <Button size="lg" className="w-full" onClick={startNew} disabled={starting}>
            {starting ? <Loader2 className="size-4 animate-spin" /> : null}
            Start a new application
            {!starting && <ArrowRight className="size-4" />}
          </Button>

          <div className="my-6 flex items-center gap-3">
            <div className="h-px flex-1 bg-border" />
            <span className="text-xs text-muted-foreground">or resume</span>
            <div className="h-px flex-1 bg-border" />
          </div>

          <div className="flex gap-2">
            <Input
              placeholder="Application ID"
              value={resumeId}
              onChange={(e) => setResumeId(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && resume()}
            />
            <Button variant="outline" onClick={resume} disabled={resuming || !resumeId.trim()}>
              {resuming ? <Loader2 className="size-4 animate-spin" /> : 'Resume'}
            </Button>
          </div>
        </Card>

        <p className="mt-6 text-center text-xs text-muted-foreground">
          Intake &amp; decision-support only -- every submission goes to manual review.
        </p>
      </motion.div>
    </div>
  )
}
