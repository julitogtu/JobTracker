'use server';

import { revalidatePath } from 'next/cache';

import type {
  ActionResult,
  CompleteJobPayload,
  CreateJobPayload,
} from '@/application/actions/action-result';
import { ApiError } from '@/infrastructure/api/http-client';
import { getContainer, organizationId } from '@/infrastructure/di/container';

function toErrorMessage(error: unknown): string {
  return error instanceof ApiError
    ? error.detail
    : 'Could not reach the JobTracker API. Is `dotnet run --project src/JobTracker.Api` running?';
}

/**
 * Mutations only. Reads are fetched in the Server Component via the DI container, never
 * through a Server Action.
 *
 * organizationId is read from server config and never accepted from the client: it is the
 * only tenant boundary the API has, since no authentication is configured.
 */
export async function createJobAction(
  payload: CreateJobPayload,
): Promise<ActionResult<{ id: string }>> {
  try {
    const id = await getContainer().createJob.execute({
      ...payload,
      organizationId: organizationId(),
    });

    revalidatePath('/jobs');
    return { ok: true, data: { id } };
  } catch (error) {
    return { ok: false, error: toErrorMessage(error) };
  }
}

export async function completeJobAction(
  payload: CompleteJobPayload,
): Promise<ActionResult<undefined>> {
  try {
    await getContainer().completeJob.execute({
      organizationId: organizationId(),
      jobId: payload.jobId,
      signatureUrl: payload.signatureUrl,
    });

    revalidatePath('/jobs');
    return { ok: true, data: undefined };
  } catch (error) {
    return { ok: false, error: toErrorMessage(error) };
  }
}
