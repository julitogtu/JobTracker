/**
 * Kept out of the 'use server' module: a file with the 'use server' directive may only
 * export async functions, so shared types live here.
 */
export type ActionResult<T = undefined> =
  | { readonly ok: true; readonly data: T }
  | { readonly ok: false; readonly error: string };

export interface CreateJobPayload {
  title: string;
  description: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
  latitude: number;
  longitude: number;
  customerId: string;
  scheduledDateUtc: string | null;
  assigneeId: string | null;
}

export interface CompleteJobPayload {
  jobId: string;
  signatureUrl: string;
}
