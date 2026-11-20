FROM golang:1.25-alpine AS builder

WORKDIR /app

# Install dependencies
COPY go.mod go.sum ./
RUN go mod download

# Copy source code
COPY . .

# Argument to specify which service to build: a folder under cmd/ (for example ingestion or importer)
ARG SERVICE_NAME
# docs/ holds the OpenAPI files a service may serve; it does not exist in every tree, so make sure the final COPY finds it.
RUN mkdir -p docs
RUN CGO_ENABLED=0 GOOS=linux go build -ldflags="-s -w" -o service ./cmd/${SERVICE_NAME}/main.go

# Final stage
FROM alpine:latest
WORKDIR /app

ARG SERVICE_NAME
ENV SERVICE_NAME=${SERVICE_NAME}

COPY --from=builder /app/service .

# Data/ is where the ingestion service keeps the GeoLite2 databases it downloads at start-up
# (MAXMIND_ACCOUNT_ID / MAXMIND_LICENSE_KEY); the files are not versioned any more, so the folder
# does not exist in the build context and has to be created here.
RUN mkdir -p /app/Data
COPY --from=builder /app/docs ./docs

# Expose common ports
EXPOSE 8080
EXPOSE 50051

ENTRYPOINT ["./service"]
