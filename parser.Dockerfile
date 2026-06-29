FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt update && apt install -y tidy

WORKDIR /foodsofengland/old-site-parser
COPY old-site-parser/src/OldSiteParser/OldSiteParser.fsproj ./src/OldSiteParser/
WORKDIR /foodsofengland/old-site-parser/src/OldSiteParser
RUN dotnet restore

COPY old-site-parser/src /foodsofengland/old-site-parser/src
WORKDIR /foodsofengland/old-site-parser/src/OldSiteParser
RUN dotnet build

CMD dotnet run