import type { CompleteJobInput, JobRepository } from '@/domain/repositories/job.repository';

export class CompleteJobUseCase {
  constructor(private readonly jobs: JobRepository) {}

  execute(input: CompleteJobInput): Promise<void> {
    return this.jobs.complete(input);
  }
}
