'use client';

import { useCallback, useMemo, useState, useTransition } from 'react';

import { completeJobAction } from '@/application/actions/job.actions';
import { JobStatus, canComplete, type Job } from '@/domain/entities/job.entity';
import { useJobsStore } from '@/presentation/stores/jobs-store.provider';

export interface UseCompleteJobResult {
  target: Job | null;
  signatureUrl: string;
  error: string | null;
  validationError: string | null;
  canSubmit: boolean;
  showStatusWarning: boolean;
  isPending: boolean;
  open: (job: Job) => void;
  close: () => void;
  setSignatureUrl: (value: string) => void;
  confirm: () => void;
}

function isAbsoluteHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

export function useCompleteJob(): UseCompleteJobResult {
  const [target, setTarget] = useState<Job | null>(null);
  const [signatureUrl, setSignatureUrl] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  const beginStatusChange = useJobsStore((state) => state.beginStatusChange);
  const commitStatusChange = useJobsStore((state) => state.commitStatusChange);
  const rollbackStatusChange = useJobsStore((state) => state.rollbackStatusChange);

  // Mirrors CompleteJobCommandValidator: absolute http/https URL, max 2048 chars.
  const validationError = useMemo<string | null>(() => {
    return signatureUrl.trim() === ''
      ? 'A signature URL is required.'
      : signatureUrl.trim().length > 2048
        ? 'Signature URL cannot exceed 2048 characters.'
        : isAbsoluteHttpUrl(signatureUrl.trim())
          ? null
          : 'Signature URL must be an absolute http or https URL.';
  }, [signatureUrl]);

  const canSubmit = useMemo(
    () => target !== null && canComplete(target.status) && validationError === null && !isPending,
    [target, validationError, isPending],
  );

  const showStatusWarning = useMemo(
    () => target !== null && !canComplete(target.status),
    [target],
  );

  const open = useCallback((job: Job): void => {
    setTarget(job);
    setSignatureUrl('');
    setError(canComplete(job.status) ? null : 'Only an in-progress job can be completed.');
  }, []);

  const close = useCallback((): void => {
    setTarget(null);
    setError(null);
  }, []);

  /**
   * Optimistic update: flip the row to Completed immediately, then commit on success or
   * roll back to the captured previous status on failure.
   */
  const confirm = useCallback((): void => {
    if (target === null || validationError !== null) {
      return;
    }

    const jobId = target.id;
    const token = beginStatusChange(jobId, JobStatus.Completed);
    setError(null);

    startTransition(async () => {
      const result = await completeJobAction({ jobId, signatureUrl: signatureUrl.trim() });

      result.ok
        ? (commitStatusChange(token), setTarget(null))
        : (rollbackStatusChange(token), setError(result.error));
    });
  }, [
    target,
    validationError,
    signatureUrl,
    beginStatusChange,
    commitStatusChange,
    rollbackStatusChange,
  ]);

  return {
    target,
    signatureUrl,
    error,
    validationError,
    canSubmit,
    showStatusWarning,
    isPending,
    open,
    close,
    setSignatureUrl,
    confirm,
  };
}
