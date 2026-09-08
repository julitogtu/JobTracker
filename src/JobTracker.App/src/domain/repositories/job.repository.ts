import type { Job, JobPage, JobStatus } from '@/domain/entities/job.entity';

export interface JobSearchCriteria {
  organizationId: string;
  searchTerm?: string | null;
  statuses?: readonly JobStatus[] | null;
  scheduledFromUtc?: string | null;
  scheduledToUtc?: string | null;
  assigneeId?: string | null;
  cursor?: string | null;
  pageSize?: number;
}

export interface CreateJobInput {
  organizationId: string;
  customerId: string;
  title: string;
  description: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
  latitude: number;
  longitude: number;
  scheduledDateUtc?: string | null;
  assigneeId?: string | null;
}

export interface CompleteJobInput {
  organizationId: string;
  jobId: string;
  signatureUrl: string;
}

export interface StartJobInput {
  organizationId: string;
  jobId: string;
}

/**
 * Port. The HTTP implementation lives in infrastructure; use cases depend on this
 * interface only, so nothing in application/ knows the API exists.
 */
export interface JobRepository {
  search(criteria: JobSearchCriteria): Promise<JobPage>;
  getById(organizationId: string, jobId: string): Promise<Job | null>;
  create(input: CreateJobInput): Promise<string>;
  complete(input: CompleteJobInput): Promise<void>;
  start(input: StartJobInput): Promise<void>;
}
