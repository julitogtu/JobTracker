import type { Job } from '@/domain/entities/job.entity';
import type { JobRepository } from '@/domain/repositories/job.repository';

export class GetJobUseCase {
  constructor(private readonly jobs: JobRepository) {}

  execute(organizationId: string, jobId: string): Promise<Job | null> {
    return this.jobs.getById(organizationId, jobId);
  }
}
