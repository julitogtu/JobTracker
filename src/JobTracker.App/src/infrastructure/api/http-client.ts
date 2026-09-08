import 'server-only';

/** Error carrying the API's ProblemDetails so callers can map status -> UI. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string,
  ) {
    super(`${status} ${title}: ${detail}`);
    this.name = 'ApiError';
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  get isConflict(): boolean {
    return this.status === 409;
  }
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

function apiBaseUrl(): string {
  return (process.env.JOBTRACKER_API_URL ?? 'http://localhost:5238').replace(/\/+$/, '');
}

function apiVersion(): string {
  return process.env.JOBTRACKER_API_VERSION ?? '1.0';
}

/** TLS codes Node raises for the untrusted ASP.NET Core development certificate. */
const DEV_CERT_CODES = new Set([
  'DEPTH_ZERO_SELF_SIGNED_CERT',
  'SELF_SIGNED_CERT_IN_CHAIN',
  'UNABLE_TO_VERIFY_LEAF_SIGNATURE',
  'ERR_TLS_CERT_ALTNAME_INVALID',
]);

function errorCode(error: unknown): string | undefined {
  const cause = (error as { cause?: { code?: string } })?.cause;
  return cause?.code ?? (error as { code?: string })?.code;
}

function describeTransportError(error: unknown, url: string): string {
  const code = errorCode(error);

  if (code !== undefined && DEV_CERT_CODES.has(code)) {
    return (
      `Node rejected the API's TLS certificate (${code}) at ${url}. The ASP.NET Core dev ` +
      'certificate is trusted by the OS but not by the CA list Node bundles. Start the app with ' +
      '`npm run dev` (it enables --use-system-ca), run `dotnet dev-certs https --trust`, or point ' +
      'JOBTRACKER_API_URL at the http profile.'
    );
  }

  return code === 'ECONNREFUSED'
    ? `Nothing is listening at ${url}. Start the API with \`dotnet run --project src/JobTracker.Api\`.`
    : `Request to ${url} failed${code === undefined ? '' : ` (${code})`}.`;
}

export interface RequestOptions {
  method?: 'GET' | 'POST';
  body?: unknown;
  /** Next.js cache hint. Reads pass { cache: 'no-store' } so the list is never stale. */
  cache?: RequestCache;
  revalidateTag?: string;
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return {};
  }
}

/**
 * Every call is server-side (Server Components and Server Actions), which is why the API
 * needs no CORS policy -- the commented-out AddCors block in Program.cs stays unnecessary.
 */
export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, cache = 'no-store', revalidateTag } = options;

  const url = `${apiBaseUrl()}${path}`;
  let response: Response;

  try {
    response = await fetch(url, {
      method,
      headers: {
        'x-api-version': apiVersion(),
        ...(body === undefined ? {} : { 'content-type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      cache,
      ...(revalidateTag === undefined ? {} : { next: { tags: [revalidateTag] } }),
    });
  } catch (error) {
    throw new ApiError(0, 'Cannot reach the JobTracker API', describeTransportError(error, url));
  }

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(
      response.status,
      problem.title ?? response.statusText,
      problem.detail ?? 'The JobTracker API returned an error.',
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}
