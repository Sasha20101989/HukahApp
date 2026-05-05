ARG APP_PATH
ARG APP_NAME

FROM node:24-alpine AS deps
WORKDIR /app
ENV PNPM_STORE_PATH=/pnpm/store
COPY package.json pnpm-workspace.yaml ./
COPY pnpm-lock.yaml ./
COPY frontend/crm-app/package.json frontend/crm-app/package.json
COPY frontend/client-app/package.json frontend/client-app/package.json
#
# We install deps without app sources to maximize Docker layer cache reuse.
# With pnpm workspaces, some setups may not materialize per-package node_modules
# unless the package directory contains files beyond package.json.
# We'll materialize deps in the build stage after copying sources.
RUN corepack enable && pnpm config set store-dir ${PNPM_STORE_PATH} && pnpm install --frozen-lockfile

FROM node:24-alpine AS build
ARG APP_PATH
ARG APP_NAME
WORKDIR /app
ENV PNPM_STORE_PATH=/pnpm/store
COPY --from=deps /pnpm /pnpm
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN corepack enable && pnpm config set store-dir ${PNPM_STORE_PATH} && pnpm install --frozen-lockfile --prefer-offline
RUN corepack enable && \
  if [ -n "${APP_NAME}" ]; then \
    pnpm --filter "${APP_NAME}" build; \
  else \
    pnpm --dir "${APP_PATH}" build; \
  fi

FROM node:24-alpine AS runtime
ARG APP_PATH
ARG APP_NAME
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV APP_PATH=${APP_PATH}
ENV APP_NAME=${APP_NAME}
COPY --from=build /app ./
CMD ["sh", "-c", "corepack enable && if [ -n \"${APP_NAME}\" ]; then pnpm --filter \"${APP_NAME}\" start; else pnpm --dir \"${APP_PATH}\" start; fi"]
