# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
# Install libc6-compat for grpc.tools and protobuf-dev for native protoc
RUN apk add --no-cache libc6-compat protobuf-dev

# Force Grpc.Tools to use the system protoc
ENV PROTOBUF_PROTOC=/usr/bin/protoc

ARG BUILD_CONFIGURATION=Release
# The project that is built and run as the API. The core runs as itself (default); a composition that adds modules to it
# (the cloud one) passes its own project name and gets its own entry assembly. Same image layout either way.
ARG HOST_PROJECT=TempooAnalytics.Api
WORKDIR /src
COPY ["src/TempooAnalytics.Api/TempooAnalytics.Api.csproj", "src/TempooAnalytics.Api/"]
COPY ["src/TempooAnalytics.ServiceDefaults/TempooAnalytics.ServiceDefaults.csproj", "src/TempooAnalytics.ServiceDefaults/"]
COPY ["src/TempooAnalytics.Analytics.Query/TempooAnalytics.Analytics.Query.csproj", "src/TempooAnalytics.Analytics.Query/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "./src/TempooAnalytics.Api/TempooAnalytics.Api.csproj"
COPY . .
# The host project: for the core this restores nothing new (cached above); a composed host is restored here, its sources are in now.
RUN dotnet restore "./src/${HOST_PROJECT}/${HOST_PROJECT}.csproj"
WORKDIR "/src/src/${HOST_PROJECT}"
RUN dotnet build "./${HOST_PROJECT}.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG HOST_PROJECT=TempooAnalytics.Api
RUN dotnet publish "./${HOST_PROJECT}.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
USER root
# tzdata: the weekly digest schedules by Europe/Madrid, and Alpine images ship without time zones.
RUN apk add --no-cache krb5-libs tzdata
USER $APP_UID
ARG ENABLE_PAYMENTS=false
ARG HOST_PROJECT=TempooAnalytics.Api
ENV EnablePayments=$ENABLE_PAYMENTS
ENV HOST_DLL=${HOST_PROJECT}.dll
WORKDIR /app
COPY --from=publish /app/publish .
# Copy Data folder into /app/Data
COPY src/TempooAnalytics.Api/Data/ ./Data/
# Copy ClickHouseScripts separately to avoid being shadowed by /app/Data volume
COPY src/TempooAnalytics.Api/Data/ClickHouseScripts/ ./ClickHouseScripts/
# /app/Data must be writable by the app user: that is where the community edition keeps jwt.key (and the GeoLite2 files),
# and a volume mounted there takes the ownership of the image directory (root, from the COPY above), so without this
# the API cannot create its signing key and stops at the first start.
USER root
RUN chown $APP_UID:$APP_UID /app/Data
USER $APP_UID
# exec: dotnet stays PID 1 as before (signals reach it); the shell only expands the entry assembly's name.
ENTRYPOINT ["sh", "-c", "exec dotnet \"$HOST_DLL\""]