# Build context is src/JobTracker.App, not the repository root: the Next app shares no sources
# with the .NET projects.
FROM node:24-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:24-alpine AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN node node_modules/next/dist/bin/next build

FROM node:24-alpine AS final
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

# The whole tree rather than a standalone bundle: `output: 'standalone'` would mean editing
# next.config.ts, and this stack is for running the app locally, not for shipping a small image.
COPY --from=build /app ./

EXPOSE 3000
# next start, not `npm run start` -- that goes through scripts/with-system-ca.mjs, which exists to
# trust the ASP.NET dev certificate. In compose the API is reached over plain HTTP on the network.
CMD ["node", "node_modules/next/dist/bin/next", "start"]
