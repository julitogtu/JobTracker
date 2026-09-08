/**
 * Client-safe domain types. Mirrors JobTracker.Domain / JobResponse on the .NET side.
 *
 * The API serialises JobStatus as a NUMBER, not a string: no JsonStringEnumConverter is
 * registered in Program.cs, and the enum is 1-based (Draft = 1). Do not compare against
 * string literals here.
 */
export const JobStatus = {
  Draft: 1,
  Scheduled: 2,
  InProgress: 3,
  Completed: 4,
  Cancelled: 5,
} as const;

export type JobStatus = (typeof JobStatus)[keyof typeof JobStatus];

export const JOB_STATUS_VALUES: readonly JobStatus[] = [
  JobStatus.Draft,
  JobStatus.Scheduled,
  JobStatus.InProgress,
  JobStatus.Completed,
  JobStatus.Cancelled,
];

export const JOB_STATUS_LABELS: Readonly<Record<JobStatus, string>> = {
  [JobStatus.Draft]: 'Draft',
  [JobStatus.Scheduled]: 'Scheduled',
  [JobStatus.InProgress]: 'In progress',
  [JobStatus.Completed]: 'Completed',
  [JobStatus.Cancelled]: 'Cancelled',
};

export function isJobStatus(value: unknown): value is JobStatus {
  return typeof value === 'number' && JOB_STATUS_VALUES.includes(value as JobStatus);
}

/** Mirrors the domain status machine so the UI never offers an impossible transition. */
export function canComplete(status: JobStatus): boolean {
  return status === JobStatus.InProgress;
}

export interface JobAddress {
  street: string;
  city: string;
  state: string;
  zipCode: string;
  latitude: number;
  longitude: number;
}

export interface Job {
  id: string;
  title: string;
  description: string;
  status: JobStatus;
  scheduledDateUtc: string | null;
  assigneeId: string | null;
  customerId: string;
  organizationId: string;
  address: JobAddress;
  photoCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

/** Cursor-paged result. The API pages by keyset, never by offset. */
export interface JobPage {
  items: Job[];
  nextCursor: string | null;
  pageSize: number;
  hasNextPage: boolean;
}
