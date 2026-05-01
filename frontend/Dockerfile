ARG APP_PATH

FROM node:24-alpine AS deps
WORKDIR /app
COPY package.json pnpm-workspace.yaml ./
COPY pnpm-lock.yaml ./
COPY frontend/crm-app/package.json frontend/crm-app/package.json
COPY frontend/client-app/package.json frontend/client-app/package.json
RUN corepack enable && pnpm install --frozen-lockfile

FROM node:24-alpine AS build
ARG APP_PATH
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN corepack enable && pnpm --dir "${APP_PATH}" build

FROM node:24-alpine AS runtime
ARG APP_PATH
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV APP_PATH=${APP_PATH}
COPY --from=build /app ./
CMD ["sh", "-c", "corepack enable && pnpm --dir ${APP_PATH} start"]
