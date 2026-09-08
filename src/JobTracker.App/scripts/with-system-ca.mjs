/**
 * Runs the Next CLI with Node's system CA store enabled.
 *
 * The .NET API's HTTPS profile (https://localhost:7163) uses the ASP.NET Core development
 * certificate. It is trusted by Windows/macOS (via `dotnet dev-certs https --trust`), but Node
 * ships its own bundled CA list and ignores the OS store by default, so a server-side fetch
 * fails with DEPTH_ZERO_SELF_SIGNED_CERT. `--use-system-ca` makes Node consult the OS store.
 *
 * NODE_OPTIONS must be set before Node starts, so it cannot come from .env.local -- hence this
 * wrapper, which re-spawns the Next CLI with the flag applied.
 */
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const FLAG = '--use-system-ca';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const nextBin = path.join(root, 'node_modules', 'next', 'dist', 'bin', 'next');

if (!existsSync(nextBin)) {
  console.error('[jobtracker] Cannot find the Next CLI. Run `npm install` first.');
  process.exit(1);
}

const supported = process.allowedNodeEnvironmentFlags.has(FLAG);
const existing = process.env.NODE_OPTIONS ?? '';

if (!supported) {
  console.warn(
    `[jobtracker] This Node (${process.version}) does not support ${FLAG}. If the API runs over ` +
      'HTTPS, either use the http profile (JOBTRACKER_API_URL=http://localhost:5238 with ' +
      '`dotnet run --launch-profile http`) or export the dev cert and set NODE_EXTRA_CA_CERTS. ' +
      'See README "Connecting to the API over HTTPS".',
  );
}

const nodeOptions =
  !supported || existing.includes(FLAG) ? existing : `${existing} ${FLAG}`.trimStart();

const child = spawn(process.execPath, [nextBin, ...process.argv.slice(2)], {
  stdio: 'inherit',
  env: { ...process.env, NODE_OPTIONS: nodeOptions },
});

child.on('exit', (code, signal) => {
  signal === null ? process.exit(code ?? 0) : process.kill(process.pid, signal);
});
