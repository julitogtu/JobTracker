import 'server-only';

import { CompleteJobUseCase } from '@/application/use-cases/complete-job.use-case';
import { CreateJobUseCase } from '@/application/use-cases/create-job.use-case';
import { GetJobUseCase } from '@/application/use-cases/get-job.use-case';
import { ListJobsUseCase } from '@/application/use-cases/list-jobs.use-case';
import type { JobRepository } from '@/domain/repositories/job.repository';
import { JobApiRepository } from '@/infrastructure/api/job-api.repository';

export interface Container {
  readonly jobRepository: JobRepository;
  readonly listJobs: ListJobsUseCase;
  readonly getJob: GetJobUseCase;
  readonly createJob: CreateJobUseCase;
  readonly completeJob: CompleteJobUseCase;
}

function build(): Container {
  const jobRepository: JobRepository = new JobApiRepository();

  return {
    jobRepository,
    listJobs: new ListJobsUseCase(jobRepository),
    getJob: new GetJobUseCase(jobRepository),
    createJob: new CreateJobUseCase(jobRepository),
    completeJob: new CompleteJobUseCase(jobRepository),
  };
}

let instance: Container | null = null;

/**
 * Composition root. `server-only` makes importing this from a Client Component a build
 * error rather than a runtime leak of the API base URL into the browser bundle.
 */
export function getContainer(): Container {
  instance ??= build();
  return instance;
}

/** Tenant scope for the demo UI. Every API call is scoped by this id. */
export function organizationId(): string {
  return process.env.JOBTRACKER_ORGANIZATION_ID ?? '8f6c1b52-1f3d-4a2e-9c77-2b1d5f0e4a10';
}
