FROM mcr.microsoft.com/dotnet/sdk:10.0 as base

ENV REPO_ROOT=/src

COPY old-site-parser /src/old-site-parser
COPY old-site-downloader /src/old-site-downloader
COPY site /src/site

WORKDIR /src/site

RUN dotnet build -c Release

# Run generator to build static files in wwwroot before publishing
RUN dotnet run -c Release --no-build -- --generate-site

RUN dotnet publish -c Release -o /app

# Copy data directory to /app/site so the app can find it at runtime
RUN mkdir -p /app/site && cp -r /src/site/data /app/site/data
RUN chmod +x /app/Site

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled

ENV REPO_ROOT=/app

WORKDIR /app
COPY --from=base --chown=app:app /app .

ENTRYPOINT [ "./Site" ]