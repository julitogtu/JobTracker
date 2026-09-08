'use client';

import { Modal } from '@/presentation/components/molecules/modal.component';
import type {
  CreateJobField,
  UseCreateJobResult,
} from '../../hooks/use-create-job.hook';

/**
 * Thin shell: no state, no handlers of its own. Everything arrives from
 * use-create-job.hook via props, and every input is a Controlled Component
 * (value + onChange owned by the parent).
 */
interface CreateJobModalProps {
  controller: UseCreateJobResult;
}

interface FieldSpec {
  field: CreateJobField;
  label: string;
  type: 'text' | 'number' | 'datetime-local';
  placeholder: string;
}

const FIELDS: readonly FieldSpec[] = [
  { field: 'title', label: 'Title', type: 'text', placeholder: 'Replace water heater' },
  { field: 'description', label: 'Description', type: 'text', placeholder: 'What needs doing' },
  { field: 'street', label: 'Street', type: 'text', placeholder: '120 Main St' },
  { field: 'city', label: 'City', type: 'text', placeholder: 'Austin' },
  { field: 'state', label: 'State', type: 'text', placeholder: 'TX' },
  { field: 'zipCode', label: 'Zip code', type: 'text', placeholder: '78701' },
  { field: 'latitude', label: 'Latitude', type: 'number', placeholder: '30.2672' },
  { field: 'longitude', label: 'Longitude', type: 'number', placeholder: '-97.7431' },
  {
    field: 'customerId',
    label: 'Customer id (GUID)',
    type: 'text',
    placeholder: '00000000-0000-0000-0000-000000000000',
  },
  {
    field: 'scheduledDateUtc',
    label: 'Scheduled (optional)',
    type: 'datetime-local',
    placeholder: '',
  },
  {
    field: 'assigneeId',
    label: 'Assignee id (optional GUID)',
    type: 'text',
    placeholder: '00000000-0000-0000-0000-000000000000',
  },
];

export function CreateJobModal({ controller }: CreateJobModalProps) {
  const { isOpen, form, errors, canSubmit, isPending, submitError, close, setField, submit } =
    controller;

  return (
    <Modal
      isOpen={isOpen}
      title="New job"
      onClose={close}
      footer={
        <>
          <button type="button" className="btn btn--ghost" onClick={close} disabled={isPending}>
            Cancel
          </button>
          <button type="button" className="btn btn--primary" onClick={submit} disabled={!canSubmit}>
            {isPending ? 'Creating...' : 'Create job'}
          </button>
        </>
      }
    >
      <div className="form-grid">
        {FIELDS.map((spec) => (
          <label className="field" key={spec.field}>
            <span className="field__label">{spec.label}</span>
            <input
              className="input"
              type={spec.type}
              placeholder={spec.placeholder}
              value={form[spec.field]}
              onChange={(event) => setField(spec.field, event.target.value)}
            />
            {errors[spec.field] === undefined ? null : (
              <span className="field__error">{errors[spec.field]}</span>
            )}
          </label>
        ))}
      </div>

      {errors.form === undefined ? null : <p className="alert alert--warn">{errors.form}</p>}
      {submitError === null ? null : <p className="alert alert--error">{submitError}</p>}
    </Modal>
  );
}
