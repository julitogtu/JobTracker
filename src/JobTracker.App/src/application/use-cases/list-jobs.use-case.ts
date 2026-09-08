import type { JobPage } from '@/domain/entities/job.entity';
import type { JobRepository, JobSearchCriteria } from '@/domain/repositories/job.repository';

export class ListJobsUseCase {
  constructor(private readonly jobs: JobRepository) {}

  execute(criteria: JobSearchCriteria): Promise<JobPage> {
    return this.jobs.search({ pageSize: 25, ...criteria });
  }
}
