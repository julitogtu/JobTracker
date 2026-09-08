'use client';

import { useCallback, useMemo, useReducer, useState, useTransition } from 'react';

import { createJobAction } from '@/application/actions/job.actions';

/* -----------------------------------------------------------------------------------------
 * useReducer: the create-job form has many fields that change together (open resets the
 * whole form, submit failure keeps it, success clears it), which is exactly the case where
 * a reducer beats a pile of useState calls.
 * -------------------------------------------------------------------------------------- */

export interface CreateJobFormState {
  title: string;
  description: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
  latitude: string;
  longitude: string;
  customerId: string;
  scheduledDateUtc: string;
  assigneeId: string;
}

export type CreateJobField = keyof CreateJobFormState;

type CreateJobFormAction =
  | { type: 'field'; field: CreateJobField; value: string }
  | { type: 'reset' };

const INITIAL_FORM: CreateJobFormState = {
  title: '',
  description: '',
  street: '',
  city: '',
  state: '',
  zipCode: '',
  latitude: '',
  longitude: '',
  customerId: '',
  scheduledDateUtc: '',
  assigneeId: '',
};

function createJobFormReducer(
  state: CreateJobFormState,
  action: CreateJobFormAction,
): CreateJobFormState {
  switch (action.type) {
    case 'field':
      return { ...state, [action.field]: action.value };
    case 'reset':
      return INITIAL_FORM;
    default:
      return state;
  }
}

function isBlank(value: string): boolean {
  return value.trim() === '';
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.trim());
}

export interface UseCreateJobResult {
  isOpen: boolean;
  form: CreateJobFormState;
  errors: Partial<Record<CreateJobField | 'form', string>>;
  canSubmit: boolean;
  isPending: boolean;
  submitError: string | null;
  open: () => void;
  close: () => void;
  setField: (field: CreateJobField, value: string) => void;
  submit: () => void;
}

export function useCreateJob(onCreated: () => void): UseCreateJobResult {
  const [isOpen, setIsOpen] = useState(false);
  const [form, dispatch] = useReducer(createJobFormReducer, INITIAL_FORM);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  // useMemo for derived validation state.
  const errors = useMemo<Partial<Record<CreateJobField | 'form', string>>>(() => {
    const next: Partial<Record<CreateJobField | 'form', string>> = {};

    isBlank(form.title) ? (next.title = 'Title is required.') : undefined;
    isBlank(form.description) ? (next.description = 'Description is required.') : undefined;
    isBlank(form.street) ? (next.street = 'Street is required.') : undefined;
    isBlank(form.city) ? (next.city = 'City is required.') : undefined;
    isBlank(form.state) ? (next.state = 'State is required.') : undefined;
    isBlank(form.zipCode) ? (next.zipCode = 'Zip code is required.') : undefined;
    isGuid(form.customerId) ? undefined : (next.customerId = 'Customer id must be a GUID.');

    const latitude = Number(form.latitude);
    const longitude = Number(form.longitude);

    Number.isFinite(latitude) && latitude >= -90 && latitude <= 90
      ? undefined
      : (next.latitude = 'Latitude must be between -90 and 90.');
    Number.isFinite(longitude) && longitude >= -180 && longitude <= 180
      ? undefined
      : (next.longitude = 'Longitude must be between -180 and 180.');

    // Domain rule from Job.Create: scheduled date and assignee are supplied together.
    isBlank(form.scheduledDateUtc) === isBlank(form.assigneeId)
      ? undefined
      : (next.form = 'Scheduled date and assignee must be provided together.');

    isBlank(form.assigneeId) || isGuid(form.assigneeId)
      ? undefined
      : (next.assigneeId = 'Assignee id must be a GUID.');

    return next;
  }, [form]);

  const canSubmit = useMemo(
    () => Object.keys(errors).length === 0 && !isPending,
    [errors, isPending],
  );

  const open = useCallback((): void => {
    dispatch({ type: 'reset' });
    setSubmitError(null);
    setIsOpen(true);
  }, []);

  const close = useCallback((): void => setIsOpen(false), []);

  const setField = useCallback(
    (field: CreateJobField, value: string): void => dispatch({ type: 'field', field, value }),
    [],
  );

  const submit = useCallback((): void => {
    if (Object.keys(errors).length > 0) {
      return;
    }

    setSubmitError(null);

    startTransition(async () => {
      const result = await createJobAction({
        title: form.title.trim(),
        description: form.description.trim(),
        street: form.street.trim(),
        city: form.city.trim(),
        state: form.state.trim(),
        zipCode: form.zipCode.trim(),
        latitude: Number(form.latitude),
        longitude: Number(form.longitude),
        customerId: form.customerId.trim(),
        scheduledDateUtc: isBlank(form.scheduledDateUtc)
          ? null
          : new Date(form.scheduledDateUtc).toISOString(),
        assigneeId: isBlank(form.assigneeId) ? null : form.assigneeId.trim(),
      });

      result.ok
        ? (setIsOpen(false), dispatch({ type: 'reset' }), onCreated())
        : setSubmitError(result.error);
    });
  }, [errors, form, onCreated]);

  return {
    isOpen,
    form,
    errors,
    canSubmit,
    isPending,
    submitError,
    open,
    close,
    setField,
    submit,
  };
}
