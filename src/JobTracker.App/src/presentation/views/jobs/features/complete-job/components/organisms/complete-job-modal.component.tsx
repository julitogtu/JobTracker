'use client';

import { Modal } from '@/presentation/components/molecules/modal.component';
import type { UseCompleteJobResult } from '../../hooks/use-complete-job.hook';

/** Thin shell -- all state and handlers live in use-complete-job.hook. */
export function CompleteJobModal({ controller }: { controller: UseCompleteJobResult }) {
  const {
    target,
    signatureUrl,
    error,
    validationError,
    canSubmit,
    showStatusWarning,
    isPending,
    close,
    setSignatureUrl,
    confirm,
  } = controller;

  return (
    <Modal
      isOpen={target !== null}
      title={target === null ? 'Complete job' : `Complete "${target.title}"`}
      onClose={close}
      footer={
        <>
          <button type="button" className="btn btn--ghost" onClick={close} disabled={isPending}>
            Cancel
          </button>
          <button type="button" className="btn btn--primary" onClick={confirm} disabled={!canSubmit}>
            {isPending ? 'Completing...' : 'Complete job'}
          </button>
        </>
      }
    >
      {showStatusWarning ? (
        <p className="alert alert--warn">
          Only an in-progress job can be completed. This one is not, so the API would return 409.
        </p>
      ) : null}

      <label className="field">
        <span className="field__label">Signature URL</span>
        <input
          className="input"
          type="url"
          placeholder="https://example.com/signatures/abc.png"
          value={signatureUrl}
          onChange={(event) => setSignatureUrl(event.target.value)}
        />
        {validationError === null ? null : (
          <span className="field__error">{validationError}</span>
        )}
      </label>

      <p className="hint">
        The row flips to Completed immediately and rolls back if the API rejects the change.
      </p>

      {error === null ? null : <p className="alert alert--error">{error}</p>}
    </Modal>
  );
}
