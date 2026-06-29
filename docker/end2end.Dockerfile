FROM mcr.microsoft.com/dotnet/sdk:10.0 as base

WORKDIR /src

COPY tests /src/tests

ENTRYPOINT ["dotnet", "test", "tests/Site.EndToEndTests/Site.EndToEndTests.fsproj", "-c", "Release"]