import type { CreateJobInput, JobRepository } from '@/domain/repositories/job.repository';

export class CreateJobUseCase {
  constructor(private readonly jobs: JobRepository) {}

  execute(input: CreateJobInput): Promise<string> {
    return this.jobs.create(input);
  }
}
