import 'server-only';

import type { Job, JobPage } from '@/domain/entities/job.entity';
import type {
  CompleteJobInput,
  CreateJobInput,
  JobRepository,
  JobSearchCriteria,
  StartJobInput,
} from '@/domain/repositories/job.repository';
import { ApiError, apiRequest } from '@/infrastructure/api/http-client';

/** Query keys are PascalCase to match ASP.NET model binding on SearchJobsQuery. */
function toSearchParams(criteria: JobSearchCriteria): string {
  const params = new URLSearchParams();
  params.set('OrganizationId', criteria.organizationId);

  if (criteria.searchTerm) {
    params.set('SearchTerm', criteria.searchTerm);
  }
  if (criteria.assigneeId) {
    params.set('AssigneeId', criteria.assigneeId);
  }
  if (criteria.scheduledFromUtc) {
    params.set('ScheduledFromUtc', criteria.scheduledFromUtc);
  }
  if (criteria.scheduledToUtc) {
    params.set('ScheduledToUtc', criteria.scheduledToUtc);
  }
  if (criteria.cursor) {
    params.set('Cursor', criteria.cursor);
  }
  if (criteria.pageSize !== undefined) {
    params.set('PageSize', String(criteria.pageSize));
  }
  // Repeated key -- binds to IReadOnlyCollection<JobStatus> on the server.
  for (const status of criteria.statuses ?? []) {
    params.append('Statuses', String(status));
  }

  return params.toString();
}

export class JobApiRepository implements JobRepository {
  async search(criteria: JobSearchCriteria): Promise<JobPage> {
    return apiRequest<JobPage>(`/api/jobs?${toSearchParams(criteria)}`);
  }

  async getById(organizationId: string, jobId: string): Promise<Job | null> {
    try {
      return await apiRequest<Job>(`/api/jobs/${organizationId}/${jobId}`);
    } catch (error) {
      return error instanceof ApiError && error.isNotFound ? null : Promise.reject(error);
    }
  }

  async create(input: CreateJobInput): Promise<string> {
    const created = await apiRequest<{ id: string }>('/api/jobs', {
      method: 'POST',
      body: {
        title: input.title,
        description: input.description,
        street: input.street,
        city: input.city,
        state: input.state,
        zipCode: input.zipCode,
        latitude: input.latitude,
        longitude: input.longitude,
        customerId: input.customerId,
        organizationId: input.organizationId,
        scheduledDateUtc: input.scheduledDateUtc ?? null,
        assigneeId: input.assigneeId ?? null,
      },
    });

    return created.id;
  }

  async complete(input: CompleteJobInput): Promise<void> {
    await apiRequest<void>('/api/jobs/complete', { method: 'POST', body: input });
  }

  async start(input: StartJobInput): Promise<void> {
    await apiRequest<void>('/api/jobs/start', { method: 'POST', body: input });
  }
}
